# Phase 09 — Gift Card Fulfilment and Code Inventory

**Recommended branch:** `phase-09-gift-card-fulfilment`

---

## Objective

Give gift cards something to deliver. A pool of redeemable codes, encrypted at rest, allocated to an order line exactly once, and structurally incapable of reaching a public endpoint.

A gift card the shop can sell but cannot fulfil is worse than one that shows as out of stock. This phase closes that gap and does so under the assumption that every code in the table is money.

---

## Current State

### What exists

Phase 02 models the gift-card **product**: `GiftCardDetail` with `BrandId`, `DenominationAmount`, `DenominationCurrency`, `DeliveryType`, plus translated region, terms and redemption steps. Phase 04 seeds fourteen of them. Phase 05 tracks their stock. Phase 08 sells them.

None of that involves a code. A customer today can buy a `$70` Steam card and receive nothing.

### What the repository already anticipates

`.gitignore` on `phase-01-foundation` reserves, with comments:

```gitignore
# Kashier merchant keys (Phase 10) and the inventory key-encryption key (Phase 05)
keyencryption*.json

# CSV imports and exports contain live, sellable gift-card codes in plaintext.
**/key-import*.csv
**/keys/*.csv
*-keys.csv
*.keys.csv
inventory-export*.csv
```

Someone already thought about this and decided plaintext code files must never be committed. Those patterns stay exactly as they are, and this phase uses them.

### What the frontend has

Nothing. There is no gift-card code UI, no "my codes" page, no order-detail section that shows a redeemed key. `DeliveryType` is rendered as a label on the product detail page and that is the whole of it.

---

## Scope

Inventory, encryption, allocation and admin import. Everything needed for the shop to hold codes safely and hand exactly one to exactly one order line.

| Component | Files |
|---|---|
| Domain | `GiftCardCode`, `GiftCardCodeStatus`, `IGiftCardCodeRepository` |
| Application | `Common/Security/ICodeProtector`; `Features/GiftCards/` — `IGiftCardFulfilmentService` / implementation, `ImportGiftCardCodesRequest` + validator, `GiftCardStockDto` |
| Infrastructure | `GiftCardCodeConfiguration`, `GiftCardCodeRepository`, `DataProtectionCodeProtector`, migration |
| API | `GiftCardCodesController` — **admin only** |

### Out of scope, deliberately

- **A customer-facing code endpoint.** See "What is not built, and why" below. The design is specified; the endpoint is not written.
- **Email delivery.** `IEmailSender` has exactly one method, `SendPasswordResetAsync`, and its only implementation logs to the console. Sending live codes through a logging sender would print money into a log file.
- **Automatic allocation on payment.** Orders never reach `Paid` until Phase 11. The allocation method exists and is called by the status transition; nothing triggers that transition automatically yet.
- **Third-party procurement.** No supplier API. Codes arrive by admin import.

---

## Database Changes

### `GiftCardCode`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `ProductId` | `uniqueidentifier` | FK, restrict delete. Which card this code is for |
| `CipherText` | `varbinary(512)` | The protected code. **Never a plaintext column** |
| `Fingerprint` | `binary(32)` | SHA-256 of the normalised plaintext, **unique filtered**. Duplicate detection without decryption |
| `ProtectionKeyId` | `nvarchar(64)` | Which key protected it, so a rotation can find what needs re-wrapping |
| `Status` | `int` | `GiftCardCodeStatus` |
| `OrderId` | `uniqueidentifier` NULL | FK, restrict delete. Set on allocation |
| `OrderLineOrdinal` | `int` NULL | Which line of that order |
| `AllocatedAt` | `datetime2` NULL | |
| `DeliveredAt` | `datetime2` NULL | |
| `VoidedReason` | `nvarchar(200)` NULL | |
| `ImportBatchId` | `uniqueidentifier` | Groups an import, so a bad batch can be voided as one |
| `RowVersion` | `rowversion` | Allocation concurrency |

```csharp
/// <summary>Lifecycle of a single redeemable gift-card code.</summary>
public enum GiftCardCodeStatus
{
    /// <summary>In the pool, sellable.</summary>
    Available,

    /// <summary>Committed to an order line, not yet handed over.</summary>
    Allocated,

    /// <summary>Handed to the customer. Terminal.</summary>
    Delivered,

    /// <summary>Withdrawn — expired, refunded by the supplier, or imported in error. Terminal.</summary>
    Voided,
}
```

Indexes:

- `(ProductId, Status)` — the allocation query, which asks for one `Available` code for a product.
- **Unique filtered on `Fingerprint`** where `IsDeleted = 0` — the same code cannot be imported twice, and a duplicate import is a constraint violation rather than two customers receiving the same key.
- **Unique filtered on `(OrderId, OrderLineOrdinal, Id)`** — not for uniqueness of the triple, but to make the per-line allocation query indexed.

`HasQueryFilter(c => !c.IsDeleted)` as everywhere. Codes are never soft-deleted in practice; `Voided` is the withdrawal path, because a soft-deleted row is invisible and an auditor needs to see it.

### There is no plaintext column, and there is no `ToString`

`GiftCardCode` exposes `CipherText` and nothing that returns the plaintext. Decryption happens in the fulfilment service, into a local variable, and the result is never assigned to a property, logged, or put on a DTO. Override `ToString()` to return the id and status only — a default record `ToString` printing `CipherText` into a log would be a bad day.

### Migration

```bash
dotnet ef migrations add AddGiftCardCodes \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

One table. Confirm the fingerprint index is unique **and** filtered, and that `CipherText` is `varbinary`, not `nvarchar` — a string column invites someone to look at it.

---

## API Contract

One controller, every action `[Authorize(Roles = Roles.Admin)]`. There is **no anonymous action on this controller**, so unlike `CatalogController` a class-level attribute is safe and is the better choice.

### `POST /gift-card-codes/import`

| | |
|---|---|
| Auth | `[Authorize(Roles = Roles.Admin)]` |
| Request | `ImportGiftCardCodesRequest { Guid ProductId, IReadOnlyList<string> Codes }` |
| Success | `200` `ImportResultDto { Guid BatchId, int Imported, int Duplicates, int Rejected }` |
| Errors | `401`, `403`, `404` `Product.NotFound`, `422` |

Codes arrive in the request body as JSON, not as a file upload. A `multipart` CSV would mean a temporary file on disk holding plaintext codes, which is precisely what the `.gitignore` patterns exist to prevent — and a temporary file is harder to guarantee gone than a request body.

The response reports counts, **never which codes were duplicates**. Echoing a rejected code back would put plaintext in a response body and in whatever logs it.

On success the product's stock is raised by `Imported`, through `IInventoryService.AdjustAsync` with `Reason = Restock`, inside the same transaction.

### `GET /gift-card-codes/stock`

| | |
|---|---|
| Success | `200` `PaginatedList<GiftCardStockDto>` |
| Errors | `401`, `403` |

`{ productId, slug, title, available, allocated, delivered, voided, sellableStock }` per gift-card product. `sellableStock` is `Product.StockQuantity`; `available` is the pool count. **They should be equal**, and the point of showing both is that when they are not, an admin finds out.

### `POST /gift-card-codes/{id}/void`

| | |
|---|---|
| Request | `VoidCodeRequest { string Reason }` |
| Success | `204` |
| Errors | `401`, `403`, `404`, `409` if already `Allocated` or `Delivered` |

Voiding an available code decrements stock by one. An allocated or delivered code cannot be voided — it belongs to an order now, and unpicking that is a refund, which this design does not have.

### What is not built, and why

**A customer code-retrieval endpoint.** The natural shape is:

```text
GET /orders/{orderId}/gift-cards   [Authorize], caller's own order only
→ 200 [{ productId, slug, title, code, redemptionSteps[] }]
→ 404 if the order is not the caller's, per Phase 08's rule
→ 409 if the order is not Paid
```

It is not written in this phase for three reasons, in order of weight:

1. **No order can reach `Paid`.** Phase 08 places orders as `Pending` and nothing advances them, because there is no cashier. An endpoint that can only ever return 409 is not an endpoint.
2. **No frontend consumes it.** There is no page, no route, no service method, and no translation key for a redeemed code. Building the endpoint would mean inventing the contract for a UI nobody has designed.
3. **It is the highest-risk endpoint in the system** — the one place plaintext codes leave the server. It should be written alongside the UI that consumes it, with the audit and rate-limiting decisions made in the same review, not months earlier against a hypothetical.

Everything it needs exists after this phase: allocation, status, ownership scoping and decryption. Adding it later is an endpoint and a test, not a redesign.

---

## Business Rules

### Encryption at rest

Use ASP.NET Core Data Protection through a narrow Application-layer seam, so nothing above Infrastructure knows how protection works:

```csharp
namespace MangaStore.Application.Common.Security;

/// <summary>Protects and unprotects gift-card codes at rest.</summary>
public interface ICodeProtector
{
    /// <summary>Encrypts a plaintext code and reports which key was used.</summary>
    (byte[] CipherText, string KeyId) Protect(string plaintext);

    /// <summary>Decrypts a stored code.</summary>
    /// <exception cref="CryptographicException">Thrown when the payload cannot be unprotected.</exception>
    string Unprotect(byte[] cipherText);

    /// <summary>SHA-256 of the normalised plaintext, for duplicate detection without decryption.</summary>
    byte[] Fingerprint(string plaintext);
}
```

Data Protection is already in the framework — no new package, no key material invented by hand, and automatic key rotation. Two things it needs before production:

- **A persisted key ring.** The default is a local folder, which means codes encrypted on one instance cannot be read on another and are lost when a container restarts. `PersistKeysToDbContext<AppDbContext>()` keeps the ring beside the data; a key vault is better where one exists.
- **Encryption of the ring itself.** `ProtectKeysWithCertificate` or the platform equivalent. The `keyencryption*.json` pattern in `.gitignore` is reserved for exactly this.

Both are configuration, and both must be recorded in the deployment notes. **A key ring that is lost makes every stored code unreadable and unsellable**, and the shop's inventory becomes a table of encrypted noise it paid for.

> Do not hand-roll AES. The tempting version — a key from configuration, `AesGcm`, a random nonce — is fine right up until nonce reuse, key rotation, or a code that needs re-wrapping. Data Protection has solved all three.

`Fingerprint` is deterministic and unkeyed on purpose: it has to be comparable across rows for the unique index. It is a duplicate detector, not a secret store, and it is not what protects the code — `CipherText` is. Normalise before hashing (trim, uppercase, strip separators) so `ABCD-EFGH` and `abcdefgh` are recognised as the same code.

### Import

1. Validate: 1 to 5000 codes, each 4–64 characters, each matching `^[A-Za-z0-9-]+$`.
2. Normalise and fingerprint every code; drop in-request duplicates.
3. Fingerprint-match against existing rows; count those as `Duplicates` and skip them.
4. Protect the rest and insert with `Status = Available` and a shared `ImportBatchId`.
5. Raise stock by the inserted count via `IInventoryService.AdjustAsync(Restock)`.
6. Commit — steps 4 and 5 in one transaction, or an import can add codes without adding sellable stock.

The unique fingerprint index is the real guarantee. Step 3 is an optimisation that produces a friendly count; the index is what makes a race impossible.

### Allocation

Called when an order becomes `Paid` — from Phase 13's admin transition today, from Phase 11's payment confirmation later.

```csharp
/// <summary>Allocates one code per gift-card unit on a paid order. Safe to call more than once.</summary>
Task<Result> AllocateForOrderAsync(Guid orderId, CancellationToken ct = default);
```

For each order line whose product is a gift card, allocate `Quantity` codes:

```csharp
// One statement, so two concurrent allocations cannot take the same code.
var claimed = await _context.GiftCardCodes
    .Where(c => c.ProductId == productId && c.Status == GiftCardCodeStatus.Available)
    .OrderBy(c => c.CreatedAt)
    .Take(quantity)
    .ExecuteUpdateAsync(s => s
        .SetProperty(c => c.Status, GiftCardCodeStatus.Allocated)
        .SetProperty(c => c.OrderId, orderId)
        .SetProperty(c => c.OrderLineOrdinal, ordinal)
        .SetProperty(c => c.AllocatedAt, now)
        .SetProperty(c => c.UpdatedAt, now), ct);

if (claimed < quantity) { /* under-supplied — see below */ }
```

Same reasoning as Phase 05's stock decrement: a guarded set-based update, not a read-then-write. Oldest first, so the pool drains in order and codes closest to any expiry go out first.

`ExecuteUpdateAsync` bypasses the audit interceptor, so `UpdatedAt` is set explicitly.

**Idempotency**: before allocating, count codes already allocated to `(orderId, ordinal)`. Allocate only the shortfall. Calling twice for the same order allocates nothing the second time and succeeds — which matters because Phase 11's webhook will be delivered more than once.

### Under-supply is an alert, not a rollback

If the pool cannot cover a paid line, the customer has already paid. Rolling back is not available.

- Allocate what there is.
- Log at **Error** with the order reference, product slug and shortfall.
- Leave the order `Paid` and the line partially fulfilled.
- Return `Result.Ok()` — the payment is not undone by a fulfilment problem.

This should be impossible, because stock and the pool are kept in step by import and allocation. Treat it as a monitoring signal that they have drifted. The `GET /gift-card-codes/stock` endpoint exists to show that drift before a customer finds it.

### Cancellation returns codes to the pool

If an order that allocated codes is cancelled, its `Allocated` codes go back to `Available` with `OrderId` cleared. **`Delivered` codes do not** — a code the customer has seen is spent, whatever happens to the order. Void those instead, so the count is right and the audit trail says why.

### Stock and the pool are two counters that must agree

`Product.StockQuantity` is what Phase 05 reserves against at checkout. The pool is what Phase 09 allocates from at payment. They are separate because they are consumed at different moments, and separate counters drift.

The rules that keep them together:

| Event | Stock | Pool |
|---|---|---|
| Import N codes | `+N` | `+N` `Available` |
| Order placed | `-Q` reserved | unchanged |
| Order paid | unchanged | `Q` → `Allocated` |
| Code delivered | unchanged | → `Delivered` |
| Order cancelled | `+Q` restored | `Allocated` → `Available` |
| Code voided (available) | `-1` | → `Voided` |

Every gift-card product stays `InventoryMode.Tracked`. Setting one to `Unlimited` would let it sell without a code behind it, which is the failure this phase exists to prevent — Phase 13's admin update must refuse that combination.

---

## Security

This is the most sensitive phase in the plan. Every row in `GiftCardCodes` is a bearer instrument.

| Concern | This phase |
|---|---|
| Authentication | `[Authorize]` on the controller class, with roles. |
| Authorization | `Roles.Admin` on every action. No customer-reachable route exists. |
| Validation | Character set, length and count bounds on import; a reason required on void. |
| Sensitive data | Encrypted at rest; no plaintext column; never logged; never on a DTO; never in an error message. |
| Concurrency | Guarded `ExecuteUpdate` for allocation; unique fingerprint index for import. |
| Rate limiting | The global policy. Import is admin-only and low-frequency. |

### The rules that matter most

1. **Never `Include` a code into a catalogue projection.** Phase 03's `ProductSummaryDto` and `ProductDetailDto` must never gain a navigation to this table. The safest form of this rule is structural: `GiftCardCode` has a `ProductId` and `Product` has **no** `Codes` navigation property, so there is nothing to include by accident.

2. **Never log a plaintext code.** Not at Debug, not in a development environment, not "temporarily". Log ids, batch ids, counts and fingerprint prefixes.

3. **Never echo a code in a response** except from the future customer endpoint, to the order's owner, over TLS.

4. **Never write codes to disk.** The import is a request body. No temporary CSV, no staging file, no export endpoint. The `.gitignore` patterns exist because someone will be handed a CSV by a supplier; the answer is that it goes into the request body from their machine and is never committed.

5. **Audit every read of a plaintext code.** When the customer endpoint is built, each decryption writes an audit row — who, which code, when. A code read twice from two IP addresses is the signal that matters, and it is unrecoverable after the fact if nobody recorded the first read.

### What an attacker would try

- **Reading the table through a catalogue endpoint.** Prevented by the missing navigation property, then by review.
- **Enumerating codes through the import's duplicate response.** Prevented by returning counts only.
- **A compromised admin account.** Not prevented — an admin can import and void. Mitigated by the audit trail and by the absence of any bulk export. There is no endpoint that returns more than one code, ever.
- **Database exfiltration.** Mitigated by encryption at rest, which is only as good as the key ring's separation from the database. If both live in the same place, this control is theatre — say so in the deployment notes.

---

## Frontend Contract

**Nothing.** No frontend surface consumes any of this, and none should be built in this phase.

Two indirect effects:

- A gift card whose pool is empty will show `outOfStock` through the normal `stockStatus` path, because import is what raises its stock. That is correct and needs no client change.
- When the customer code endpoint is eventually built, the client work is a section on the order detail page, gated on `status === 'paid'`. `GiftCardDetail.redemptionSteps` and `terms` are already modelled, already translated and already rendered on the product page, so most of the presentation exists.

---

## Testing

### Unit tests

| Test | Asserts |
|---|---|
| `CodeProtectorTests.ProtectThenUnprotect_RoundTrips` | |
| `CodeProtectorTests.SamePlaintext_ProducesDifferentCipherText` | Data Protection is randomised; two rows for the same code do not look alike. |
| `CodeProtectorTests.Fingerprint_IsStableAcrossFormatting` | `abcd-efgh`, `ABCDEFGH` and ` ABCD-EFGH ` share a fingerprint. |
| `CodeProtectorTests.Fingerprint_DiffersForDifferentCodes` | |
| `GiftCardCodeTests.ToStringDoesNotContainCipherText` | The accidental-log guard. |
| `FulfilmentServiceTests.Import_SkipsDuplicatesAndReportsCounts` | And the response contains no code text. |
| `FulfilmentServiceTests.Import_RaisesStockByImportedCount` | Not by the submitted count. |
| `FulfilmentServiceTests.Import_RejectsBadCharacters` | 422. |
| `FulfilmentServiceTests.Allocate_TakesOldestFirst` | |
| `FulfilmentServiceTests.Allocate_Twice_AllocatesOnce` | The webhook-redelivery case. |
| `FulfilmentServiceTests.Allocate_UnderSupplied_LogsErrorAndSucceeds` | Payment is not undone by fulfilment. |
| `FulfilmentServiceTests.Allocate_IgnoresNonGiftCardLines` | |
| `FulfilmentServiceTests.Cancel_ReturnsAllocatedButNotDelivered` | Delivered codes are voided instead. |
| `FulfilmentServiceTests.Void_AllocatedCode_Returns409` | |
| `FulfilmentServiceTests.Void_AvailableCode_DecrementsStock` | |

### Integration tests

| Test | Asserts |
|---|---|
| `GiftCardCodeApiTests.AllEndpoints_RequireAdmin` | Anonymous → 401; `Customer` → 403 with `ProblemDetails.Title == "Auth.Forbidden"`. |
| `GiftCardCodeApiTests.Import_DuplicateFingerprint_ViolatesUniqueIndex` | The index is really filtered and really unique. |
| `GiftCardCodeApiTests.StockEndpoint_ShowsPoolAndSellableSeparately` | And they match after an import. |
| `GiftCardCodeApiTests.NoCatalogueResponseContainsAnyCodeField` | Fetch every catalogue endpoint for a gift card, assert no `code`, `cipherText` or `secret` member anywhere in the JSON. **The leak test, and the most valuable test in the phase.** |
| `GiftCardCodeApiTests.ImportResponse_ContainsNoCodeText` | Import a known code twice; assert the string is absent from the response body. |

### The log test

Worth writing even though it feels unusual:

```csharp
public async Task Import_WritesNoPlaintextCodeToAnyLogSink()
```

Capture Serilog into an in-memory sink for the duration of an import, then assert none of the submitted code strings appear in any message or property. It catches the one mistake that is invisible in review and catastrophic in production.

### Edge cases

- Importing zero codes: 422.
- Importing 5001: 422.
- Importing for a manga product: 422 — the product must be `Kind = GiftCard`.
- Importing the same code in the same request twice: counted once, no error.
- Allocating for an order with no gift-card lines: succeeds, allocates nothing.
- Allocating for an order that is not `Paid`: 409. Allocation follows payment, never precedes it.
- Voiding an already-voided code: 409.
- A code that fails to decrypt (key ring lost or rotated away): the service must fail loudly with a distinct error, not return an empty string. Losing the key ring is a data-loss event and it should look like one.

---

## Acceptance Criteria

- [ ] `GiftCardCode` entity with encrypted `CipherText`, unique filtered `Fingerprint`, status lifecycle and batch id; migration `AddGiftCardCodes`.
- [ ] **No plaintext column**, no `Codes` navigation on `Product`, and `ToString()` overridden to exclude ciphertext.
- [ ] `ICodeProtector` in Application; Data Protection implementation in Infrastructure; key-ring persistence and encryption recorded in the deployment notes.
- [ ] `GiftCardCodesController` with three admin-only actions and no anonymous action.
- [ ] Import validates, deduplicates by fingerprint, protects, inserts and raises stock in one transaction; the response carries counts only.
- [ ] Allocation is a guarded `ExecuteUpdateAsync`, oldest first, idempotent per `(orderId, ordinal)`, and sets `UpdatedAt` explicitly.
- [ ] Under-supply logs at Error and succeeds; it does not roll back a payment.
- [ ] Cancellation returns `Allocated` codes and voids `Delivered` ones.
- [ ] The stock/pool reconciliation table is implemented and surfaced by `GET /gift-card-codes/stock`.
- [ ] **No catalogue response contains any code field**, proved by a test that inspects raw JSON.
- [ ] **No log line contains a plaintext code**, proved by a test with a capturing sink.
- [ ] No customer-facing code endpoint was built, and the PR states why.
- [ ] No code is written to disk anywhere in the implementation.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 02 - GiftCardDetail and Product.
  Phase 05 - IInventoryService.AdjustAsync, for the stock/pool linkage.
  Phase 08 - Order and OrderLine, for allocation targets.

Blocks:
  Phase 11 (payment preparation) - payment confirmation calls AllocateForOrderAsync.
  Phase 12 (dashboard)           - pool health is a statistic worth showing.
  Phase 13 (admin CRUD)          - must refuse InventoryMode.Unlimited on a gift card.

Can be implemented independently:
  Partly. The inventory, encryption and import work needs only Phases 02 and 05.
  Allocation needs Phase 08. Splitting it that way is reasonable if Phase 08
  is not ready, but the pool without allocation is a half-feature and should
  not be merged as done.
```
