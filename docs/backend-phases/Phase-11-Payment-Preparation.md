# Phase 11 — Payment / Cashier Preparation

**Recommended branch:** `phase-11-payment-preparation`

---

## Objective

Build everything a payment integration needs **except** the payment integration.

The frontend has a complete checkout UI. The backend can place orders. There is no cashier, no merchant account, and no decision on file about which provider to use. This phase makes the seam where one plugs in, moves an order from `Pending` to `Paid` through a single auditable path, and stops there.

**No provider is chosen. No payment is processed. No endpoint pretends money moved.**

---

## Current State

### What exists

Two pieces of the foundation branch were added for this and have had no consumer since:

- **`ApiControllerBase.HandleOk(Result)`** — 200 with no body. Its commit message says why: *"for the payment webhook ack. 204 is wrong for a PSP callback and some providers retry on non-200."*
- **`ResilienceDefaults.ConfigureNonIdempotentExternal`** — total timeout 30s, **`MaxRetryAttempts = 0`**, because *"retrying a payment-session create is a duplicate charge against a real customer."* The `ReadOnlyExternal` sibling retries; this one deliberately does not.

`ScopedBackgroundService` is also there, unused, and is the right base for the reconciliation job described below.

Phase 08 places orders as `Pending` and nothing advances them.

### What the repository hints at

`.gitignore` on the foundation branch reserves `kashier*.local.json` with the comment *"Kashier merchant keys (Phase 10)"*. Kashier is an Egyptian payment gateway, which fits a shop pricing in EGP.

**That is a hint, not a decision.** There is no Kashier code, no package reference, no configuration key, and no merchant account in evidence. This phase stays provider-agnostic and records the question rather than answering it — see "The decision this phase does not make".

### What the frontend has

A complete four-step checkout at `/checkout`: address, shipping, payment, review. The payment step collects `cardName`, `cardNumber`, `expiry` and `cvc`, formats them, shape-validates them, and **transmits nothing**. Its own doc-comment: *"Payment is deliberately inert. The fields are formatted and shape-validated, nothing is transmitted, and no card data is stored anywhere."* The UI says the same to the customer.

None of it is reachable. `CartPage.checkout()` raises a toast and stays on the cart — commit `8bd4690`, *"Stop the cart pretending it can take payment"*. Wiring it up is a one-line change, left undone deliberately so the absence of a payment backend is a visible decision rather than a half-built flow someone might mistake for working.

---

## Scope

Provider-agnostic internals only.

| Component | Files |
|---|---|
| Domain | `PaymentIntent`, `PaymentIntentStatus`, `IPaymentIntentRepository`; `OrderStatus` transition rules |
| Application | `Common/Payments/IPaymentGateway`, `PaymentSessionRequest`, `PaymentSessionResult`, `PaymentConfirmation`; `Features/Payments/` — `IPaymentConfirmationService` / implementation; `PaymentOptions` |
| Infrastructure | `PaymentIntentConfiguration`, `PaymentIntentRepository`, `UnconfiguredPaymentGateway`, migration |
| API | **Nothing.** No controller, no route |

### Explicitly not built

| Not built | Why |
|---|---|
| A provider client | None is chosen, and inventing one against a guessed API is the definition of an invented integration. |
| **The webhook endpoint** | A webhook is unauthenticated by nature and is secured by a provider-specific signature scheme. With no provider there is no scheme, so the endpoint would be an anonymous route that marks orders paid. That is the single worst thing this plan could ship. The shape is documented below; the route is not written. |
| Card handling of any kind | The shop must never see a PAN. Redirect or provider-hosted fields only. |
| Refunds | No provider, and no refund flow anywhere in the frontend. |
| A payment-status endpoint | No client polls one. |

---

## Database Changes

### `PaymentIntent`

One row per attempt to pay for an order. An order can have several — a customer who abandons a redirect and tries again produces two, and both need to be reconcilable against the provider afterwards.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `OrderId` | `uniqueidentifier` | FK, restrict delete |
| `Provider` | `nvarchar(40)` | Free text, e.g. `kashier`. Not an enum — an enum would have to be edited to add a provider, and that is a configuration change, not a code change |
| `ProviderReference` | `nvarchar(120)` NULL | The provider's own id. **Unique filtered on `(Provider, ProviderReference)`** |
| `Status` | `int` | `PaymentIntentStatus` |
| `Amount` | `decimal(18,2)` | Snapshot of `Order.Total` when the intent was created |
| `Currency` | `nchar(3)` | |
| `IdempotencyKey` | `nvarchar(80)` | Ours, sent to the provider. **Unique filtered** |
| `FailureCode` | `nvarchar(80)` NULL | The provider's code, for support |
| `ConfirmedAt` | `datetime2` NULL | |
| `RowVersion` | `rowversion` | |

```csharp
/// <summary>Lifecycle of one attempt to pay for an order.</summary>
public enum PaymentIntentStatus
{
    /// <summary>Created locally; no provider session yet.</summary>
    Created,

    /// <summary>Provider session opened; the customer is away paying.</summary>
    Pending,

    /// <summary>The provider confirmed payment. Terminal.</summary>
    Succeeded,

    /// <summary>The provider declined or the customer abandoned. Terminal.</summary>
    Failed,

    /// <summary>Superseded by a later attempt, or the order was cancelled. Terminal.</summary>
    Cancelled,
}
```

**No card data column. No token column. No `RawProviderPayload` column.** A provider payload is the most tempting thing to persist for debugging and the most likely to contain something that must not be stored. If a payload is needed for support, log a redacted subset with a short retention, not a database column with none.

`Amount` is a snapshot because the confirmation has to be checked against what was asked for, not against what the order says now.

### `OrderStatusEvent` gains an actor

Add `Source` (`nvarchar(40)`) to Phase 08's owned `OrderStatusEvent`: `system`, `payment`, or `admin`. When an order shows as paid, the first question is who said so, and the timeline should answer it without a separate audit table.

The client's `OrderStatusEvent` interface has only `status` and `occurredAt`, so **`Source` is not published** — it exists for support and admin views.

### Migration

```bash
dotnet ef migrations add AddPaymentIntents \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

---

## API Contract

**No endpoint is added by this phase.**

The internal seams other code calls:

```csharp
namespace MangaStore.Application.Common.Payments;

/// <summary>Opens and verifies payment sessions with an external provider.</summary>
public interface IPaymentGateway
{
    /// <summary>Name of the provider this gateway talks to.</summary>
    string Provider { get; }

    /// <summary>Opens a payment session and returns where to send the customer.</summary>
    Task<Result<PaymentSessionResult>> CreateSessionAsync(PaymentSessionRequest request, CancellationToken ct = default);

    /// <summary>Verifies a callback's authenticity and extracts what it asserts.</summary>
    /// <remarks>Implementations must verify a signature. A gateway that trusts the payload is not a gateway.</remarks>
    Result<PaymentConfirmation> VerifyCallback(IReadOnlyDictionary<string, string> headers, string rawBody);

    /// <summary>Asks the provider what actually happened, ignoring anything a callback claimed.</summary>
    Task<Result<PaymentConfirmation>> FetchStatusAsync(string providerReference, CancellationToken ct = default);
}
```

`PaymentConfirmation` is `(string ProviderReference, string IdempotencyKey, decimal Amount, string Currency, bool Succeeded, string? FailureCode)`.

```csharp
namespace MangaStore.Application.Features.Payments;

/// <summary>The single path by which an order becomes Paid.</summary>
public interface IPaymentConfirmationService
{
    /// <summary>Applies a verified confirmation to its order. Safe to call more than once.</summary>
    Task<Result> ConfirmAsync(PaymentConfirmation confirmation, CancellationToken ct = default);

    /// <summary>Marks an intent failed without touching the order.</summary>
    Task<Result> FailAsync(string providerReference, string? failureCode, CancellationToken ct = default);
}
```

### `UnconfiguredPaymentGateway` — the only implementation

Registered when no provider is configured, which is always, today:

```csharp
/// <inheritdoc cref="IPaymentGateway"/>
/// <remarks>The gateway used when no payment provider is configured. It refuses every request.</remarks>
public sealed class UnconfiguredPaymentGateway : IPaymentGateway
{
    /// <inheritdoc/>
    public string Provider => "none";

    /// <inheritdoc/>
    public Task<Result<PaymentSessionResult>> CreateSessionAsync(PaymentSessionRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Fail<PaymentSessionResult>(
            ResultError.Failure("Payment", "No payment provider is configured.")));

    // VerifyCallback and FetchStatusAsync fail the same way.
}
```

It **fails**. It does not succeed, does not simulate, and does not have a "development mode" that marks orders paid. A gateway that pretends in development is a gateway that will pretend in production the first time a configuration key is missing.

`ResultError.Failure` maps to 400 through `ApiControllerBase`. There is no caller today, so no status reaches a client; when one exists it should map this to **503**, because the shop is not refusing the request — it is unable to serve it. That is a one-line addition to the error mapping when a provider lands.

---

## Business Rules

### One path to `Paid`

`IPaymentConfirmationService.ConfirmAsync` is the only code in the system that sets `OrderStatus.Paid`. Not the order service, not a controller, not an admin endpoint directly — Phase 13's admin transition calls into this same method.

One path means one place where the gift-card allocation, the status event and the audit happen, and one place to look when an order is paid and should not be.

`ConfirmAsync`, in one transaction:

1. Find the intent by `(Provider, ProviderReference)`. Unknown → fail, log at Warning. An unknown reference is either a misrouted callback or an attack.
2. Already `Succeeded` → return `Result.Ok()` and stop. **Idempotent**, because webhooks are redelivered.
3. **Check the amount and currency against `PaymentIntent.Amount`/`Currency`.** A mismatch is never a partial payment to be accepted — fail, log at Error, leave the order `Pending`. This is the check that catches a tampered callback.
4. Set the intent `Succeeded`, stamp `ConfirmedAt`.
5. Move the order `Pending → Paid` and append an `OrderStatusEvent` with `Source = payment`.
6. Call `IGiftCardFulfilmentService.AllocateForOrderAsync` if the order has gift-card lines.
7. Cancel any other `Pending` intents on the same order.
8. Commit.

Step 6 inside the transaction is deliberate: an order marked paid with no codes allocated is a customer who paid for nothing, and the allocation is a local database write with no external call, so there is no reason to defer it.

### Legal status transitions

| From | To | Who |
|---|---|---|
| `Pending` | `Paid` | `IPaymentConfirmationService` only |
| `Pending` | `Cancelled` | Admin, or the reconciliation job on an abandoned intent |
| `Paid` | `Shipped` | Admin |
| `Shipped` | `Delivered` | Admin |
| `Paid` or `Shipped` | `Cancelled` | Admin |
| `Delivered` | anything | **Never** |
| `Cancelled` | anything | **Never** |

Encode this as a table in Domain, not as `if` statements spread across services. Phase 13's admin endpoint consults the same table. Any transition to `Cancelled` from a state that decremented stock calls `IInventoryService.ReleaseAsync`, which Phase 05 made idempotent.

### Never trust a callback's contents

A callback says what happened. It is not evidence that it happened.

- **Verify the signature first.** No signature, wrong signature, or a signature over a body that was re-serialised before verification → reject before any lookup. Verify over the **raw** body bytes; a framework that parses and re-serialises JSON will produce a different byte sequence and a failing signature.
- **Confirm against the provider** with `FetchStatusAsync` for anything above a configured amount, or for every payment if the provider's API allows it cheaply. The callback tells you to look; the provider's API tells you what is true.
- **Check the amount.** Always, against the intent's snapshot.

### Reconciliation, not hope

Webhooks are lost. A payment can succeed while its callback never arrives, leaving a paid customer with a `Pending` order.

`ScopedBackgroundService` — on the foundation branch, unused, waiting for exactly this — is the base for a job that every few minutes takes `Pending` intents older than a threshold, calls `FetchStatusAsync`, and feeds any success through the same `ConfirmAsync`. Its scope-per-tick and per-tick exception isolation are what that job needs.

Not written now, because there is nothing to fetch from. Named here because it is a requirement of the integration, not an enhancement, and integrations that skip it discover the gap through customer complaints.

### The frontend flow, for when it exists

```text
Cart → Checkout → POST /orders           → 201 Pending order
                → POST /orders/{id}/payment-session
                                          → 200 { redirectUrl }
                → browser leaves for the provider
                → provider → webhook → ConfirmAsync → order Paid
                → browser returns to /checkout/confirmation/{orderId}
                → GET /orders/{id} → status: "paid"
```

The **return URL is not the confirmation**. A customer can close the tab, and the provider's redirect can be forged. The webhook is the source of truth; the return URL only brings the customer back to a page that then reads the order.

The confirmation page should tolerate a `Pending` order for a few seconds and poll — the webhook and the redirect race, and the redirect usually wins.

### Cards never touch this server

Redirect flow, or provider-hosted fields that tokenise in the browser. The API must never receive a PAN, a CVC or an expiry.

The frontend's current card fields are a mock and must be **replaced**, not wired up. Leaving them and posting them anywhere would put the shop in PCI scope for no benefit.

### The decision this phase does not make

**Which provider.** The evidence points at Kashier — the `.gitignore` reserves its key file and the shop is plausibly Egyptian — but there is no code, no account and nothing written down.

Record it as an open question with what each answer costs:

| Question | Why it matters |
|---|---|
| Which provider? | Determines the signature scheme, the session API, and whether `FetchStatusAsync` is cheap enough to call every time |
| Redirect or hosted fields? | Redirect is simpler and keeps the shop out of PCI scope; hosted fields keep the customer on the site |
| Trading currency? | Every seeded price is `USD`. A `4025 EGP` selling price for a `70 USD` card is supported by the schema and unsupported by the seed data and the frontend's expectations |
| Is 3-D Secure required? | Adds a second redirect and a pending state that can last minutes |
| What is the refund policy? | Nothing in this plan implements refunds, and the stock-restore rule assumes cancellation without one |

Answering the first two is a prerequisite for the phase that follows this one. Answering the rest can wait.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | No endpoint. When the session endpoint lands it is `[Authorize]` and scoped to the caller's own order. |
| Authorization | The order must belong to the caller. 404, not 403, per Phase 08. |
| Validation | Amount and currency checked against the intent on every confirmation. |
| Sensitive data | No card data, no tokens, no raw payloads stored. Merchant keys come from configuration and never from the repository — `kashier*.local.json` is already ignored. |
| Concurrency | `RowVersion` on the intent; unique filtered indexes on `(Provider, ProviderReference)` and on our idempotency key; `ConfirmAsync` idempotent by design. |
| Rate limiting | The webhook, when it exists, needs its own policy — a provider retries, and an attacker can too. Signature verification comes first, so a forged flood is cheap to reject. |

### The webhook checklist — for whoever writes it

Not written here. When it is:

- [ ] `[AllowAnonymous]`, and the **only** anonymous write endpoint in the system.
- [ ] Signature verified over the **raw body**, before parsing, using `[FromBody] string` or a raw-body read.
- [ ] Constant-time signature comparison.
- [ ] Replay window enforced on the provider's timestamp header.
- [ ] Returns `200` via `HandleOk` — **not `204`** — because some providers treat a non-200 as a failure and retry. This is why `HandleOk` exists.
- [ ] Returns `200` even for a payload it decides to ignore, so the provider stops retrying something that will never succeed. Log it; do not signal failure.
- [ ] Never echoes any part of the payload in the response body.
- [ ] Its own rate-limit policy, partitioned by source IP.
- [ ] Idempotent — assume every callback arrives at least twice.
- [ ] Covered by a test that a **forged** signature is rejected. That test is the whole security of the endpoint.

### What an attacker would try

- **Forging a callback** to mark an order paid. Defeated by signature verification, then by the amount check, then by `FetchStatusAsync`.
- **Replaying a real callback** for a different order. Defeated by the reference lookup and idempotency.
- **Under-paying and claiming success.** Defeated by the amount check against the intent snapshot.
- **Racing two sessions** on one order and paying the cheaper. Defeated by step 7 — confirming one intent cancels the others — and by `Amount` being snapshotted per intent.

---

## Frontend Contract

**Nothing is consumed, and nothing changes.**

`CartPage.checkout()` stays a toast:

```ts
protected checkout(): void {
  this.notifications.info('cart.checkoutComingSoon');
}
```

Do not wire it up. Do not enable the payment step. Do not add a "test mode". The frontend is deliberately honest about the gap, and this phase does not close it — it prepares for the phase that will.

What is ready for that phase: the four-step checkout, the address form matching `Address` field for field, `PlaceOrderRequest`, `OrderService.place()`, the confirmation route `/checkout/confirmation/:orderId`, and `authGuard` on both.

What will need work: replacing the mock card fields with a redirect or hosted fields, calling the session endpoint, handling the return, and polling the confirmation page while the webhook races the redirect.

---

## Testing

Everything here is testable without a provider, because the provider is behind an interface.

### Unit tests

| Test | Asserts |
|---|---|
| `PaymentConfirmationTests.Confirm_MovesPendingOrderToPaid` | And appends an event with `Source = payment`. |
| `PaymentConfirmationTests.Confirm_Twice_IsIdempotent` | One transition, one event, one allocation call. **The webhook-redelivery case.** |
| `PaymentConfirmationTests.Confirm_AmountMismatch_FailsAndLeavesOrderPending` | Under-payment and over-payment both. **The tampered-callback case.** |
| `PaymentConfirmationTests.Confirm_CurrencyMismatch_Fails` | |
| `PaymentConfirmationTests.Confirm_UnknownReference_FailsAndLogsWarning` | |
| `PaymentConfirmationTests.Confirm_AllocatesGiftCardCodes` | Cross-phase, with `IGiftCardFulfilmentService` substituted. |
| `PaymentConfirmationTests.Confirm_CancelsOtherPendingIntents` | |
| `PaymentConfirmationTests.Confirm_OrderAlreadyCancelled_Fails` | Paying for a cancelled order is a support case, not a state change. |
| `OrderStatusTransitionTests.EveryIllegalTransitionIsRefused` | Drive the full matrix. `Delivered` and `Cancelled` accept nothing. |
| `UnconfiguredGatewayTests.EveryMethodFails` | **No method succeeds, in any environment.** |
| `UnconfiguredGatewayTests.HasNoDevelopmentBypass` | Reflection over the type: no environment check, no configuration branch that could make it succeed. Unusual, and worth it — this is the class most likely to grow a helpful shortcut. |

### Integration tests

| Test | Asserts |
|---|---|
| `PaymentIntentTests.DuplicateProviderReference_ViolatesUniqueIndex` | |
| `PaymentIntentTests.ConfirmPersistsAcrossRequests` | |
| `PaymentPreparationTests.NoRouteMarksAnOrderPaid` | Enumerate every route via `EndpointDataSource`; assert none is anonymous and writes. **The test that keeps this phase honest.** |
| `PaymentPreparationTests.NoAnonymousWriteEndpointsExist` | Same sweep, stated as a rule. Phase 16 repeats it. |

### Edge cases

- Confirmation for an order already `Shipped`: idempotent success — a redelivered webhook for an order that has since moved on is normal.
- Confirmation for a `Cancelled` order: fails and logs at Error. Money arrived for something the shop cancelled, and that needs a human.
- Two callbacks for two intents on one order arriving simultaneously: one wins on `RowVersion`; the loser finds its intent cancelled and returns success.
- An order with no gift-card lines: allocation is a no-op.
- An intent whose order was deleted: impossible — restrict delete on the FK.

---

## Acceptance Criteria

- [ ] `PaymentIntent` entity, configuration, repository and `DbSet`; migration `AddPaymentIntents`.
- [ ] Unique filtered indexes on `(Provider, ProviderReference)` and on `IdempotencyKey`.
- [ ] **No column stores card data, a provider token, or a raw payload.**
- [ ] `OrderStatusEvent.Source` added and not published in `OrderDto`.
- [ ] `IPaymentGateway` and `IPaymentConfirmationService` defined in Application with full XML docs.
- [ ] `UnconfiguredPaymentGateway` is the only implementation, fails every call, and has no environment or configuration branch that could make it succeed.
- [ ] `ConfirmAsync` is the **only** code that sets `OrderStatus.Paid`.
- [ ] `ConfirmAsync` is idempotent, checks amount and currency against the intent, allocates gift-card codes, cancels sibling intents, and runs in one transaction.
- [ ] The status transition matrix lives in Domain and refuses every illegal transition.
- [ ] **No controller, no route, and no webhook endpoint was added.** A test proves no anonymous write endpoint exists.
- [ ] The webhook checklist is in this document for whoever implements it.
- [ ] `CartPage.checkout()` is unchanged and still a toast.
- [ ] The provider decision and the other four open questions are recorded in the PR.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 01 - HandleOk and ResilienceDefaults were added there for this phase.
  Phase 05 - IInventoryService.ReleaseAsync, for cancellation transitions.
  Phase 08 - Order, OrderStatus, OrderStatusEvent.
  Phase 09 - IGiftCardFulfilmentService.AllocateForOrderAsync.

Blocks:
  Phase 13 (admin CRUD) - the admin status transition calls ConfirmAsync
                          rather than setting Paid itself.
  The future payment-integration phase, which is out of this plan's scope
  until a provider is chosen.

Can be implemented independently:
  No. It also cannot be COMPLETED in the sense of taking a payment - that
  is the point. It ends with a working seam and a documented gap.
```
