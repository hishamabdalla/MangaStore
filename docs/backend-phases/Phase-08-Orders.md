# Phase 08 — Orders

**Recommended branch:** `phase-08-orders`

---

## Objective

Turn a cart into a record of what was agreed. Reprice every line server-side, re-validate the coupon, commit the stock, and write an order whose numbers cannot be rewritten by anything that happens to the catalogue afterwards.

This is where the transactional weight of the system sits. Everything before it can be retried; an order cannot.

---

## Current State

### What exists

Phase 06 gives a server-owned cart and `ICartPricingCalculator`. Phase 07 gives coupon validation and `ValidateForCartAsync`. Phase 05 gives `IInventoryService.ReserveAsync` and `ReleaseAsync`, with a `StockMovement.OrderId` column that has no foreign key yet because `Orders` did not exist.

### What is missing

No `Order`, no `OrderLine`, no status, no reference, no idempotency. Nothing has ever opened an explicit transaction — every write so far has been a single `SaveChangesAsync`.

### The frontend

`InMemoryOrderService.place()` already mirrors what this endpoint must do: it re-resolves prices from the catalogue rather than trusting the form, re-validates the coupon (a rejection fails the order rather than silently dropping the discount), and snapshots `title` and `unitPrice` onto each line.

It also does one thing this phase must **not** copy: it sets `status: 'paid'` immediately with history `[pending, paid]`. That is a mock standing in for a payment gateway that does not exist.

**Checkout is unreachable from the UI.** `CartPage.checkout()` raises a toast and goes nowhere — commit `8bd4690`, "Stop the cart pretending it can take payment". The checkout page, its four steps and its client code are all written and tested; nothing calls them. Wiring it up is a one-line change, deliberately, so that the absence of a payment backend is a visible decision rather than a half-built flow.

That means **this phase ships an endpoint the storefront will not call yet**, and that is fine. Order history at `/account/orders` becomes real immediately; placement waits for Phase 11 and a real cashier.

---

## Scope

| Component | Files |
|---|---|
| Domain | `Order`, `OrderLine`, `OrderStatusEvent`, `Address`, `OrderStatus`, `ShippingMethod`, `IOrderRepository` |
| Application | `Features/Orders/` — `OrderDto`, `OrderLineDto`, `OrderStatusEventDto`, `AddressDto`, `PlaceOrderRequest` + validator, `OrderProfile`, `IOrderService` / `OrderService` |
| Infrastructure | Configurations, `OrderRepository`, migration (including the deferred `StockMovement.OrderId` FK and one new `Cart` column) |
| API | `OrdersController` |

### Out of scope

- **Payment.** No provider, no fake processing, no status that claims money moved. Phase 11.
- **Admin status transitions.** `PUT /orders/{id}/status` is Phase 13.
- **Gift-card code delivery.** Phase 09 hooks into the order lifecycle.
- **Refunds and partial cancellation.** Neither exists in the frontend.

---

## Database Changes

### `Order`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `UserId` | `uniqueidentifier` | Bare, indexed. Every query scopes by it |
| `Reference` | `nvarchar(16)` | **Unique filtered.** Human-facing, e.g. `MS-2A7F41` |
| `Status` | `int` | `OrderStatus` |
| `PlacedAt` | `datetime2` | |
| `Subtotal` | `decimal(18,2)` | |
| `Discount` | `decimal(18,2)` | **Snapshot.** What the coupon was worth that day |
| `CouponCode` | `nvarchar(40)` NULL | **Snapshot.** The code that earned it |
| `Shipping` | `decimal(18,2)` | |
| `Tax` | `decimal(18,2)` | |
| `Total` | `decimal(18,2)` | |
| `Currency` | `nchar(3)` | |
| `ShippingMethod` | `int` | |
| `IdempotencyKey` | `nvarchar(80)` NULL | Unique filtered on `(UserId, IdempotencyKey)` |
| `RowVersion` | `rowversion` | For admin status transitions in Phase 13 |
| *owned* `ShippingAddress` | | `Address`, columns prefixed `Shipping` |

`Discount` and `CouponCode` are snapshots like the line prices: what the coupon was worth on the day, not a value to be recomputed from a coupon that may since have changed.

Indexes: `(UserId, PlacedAt DESC)` for history; unique filtered on `Reference`; unique filtered on `(UserId, IdempotencyKey)` where the key is not null.

### `OrderLine` — owned by `Order`

| Column | Type | Notes |
|---|---|---|
| `ProductId` | `uniqueidentifier` | Reference only — **no foreign key** |
| `Slug` | `nvarchar(160)` | Snapshot |
| `Title` | `nvarchar(200)` | Snapshot, already localized |
| `UnitPrice` | `decimal(18,2)` | Snapshot |
| `Quantity` | `int` | |

**`Title` and `UnitPrice` are copied at purchase, never joined from `Product`.** This is not denormalisation for speed; it is correctness. An order is a record of what was agreed, and joining to the live product turns last year's receipt into a lie the first time something is repriced.

No foreign key on `ProductId` for the same reason: a hard-deleted product must not be able to take an order's history with it. Soft delete makes that unlikely, not impossible.

> `Title` is stored in **one** language — the one the customer was shopping in when they bought. An order is a historical record, not a localized view, and re-localizing a receipt would change what it says was bought. `InMemoryOrderService` snapshots `product.title.en`; do better and snapshot the language the request asked for.

### `OrderStatusEvent` — owned by `Order`

`Status`, `OccurredAt`. Backs the progress timeline on the order detail page. Written on placement (`Pending`) and on every subsequent transition.

### `Address` — an owned type, not an entity

`FullName`, `Line1`, `Line2?`, `City`, `PostalCode`, `Country`, `Phone`. No `Id`, no lifetime of its own, no address book. The frontend's checkout form types it in each time and nothing reads it back except the order it belongs to.

### Two changes to existing tables

1. **`StockMovement.OrderId`** gains its foreign key to `Orders`, deferred from Phase 05. Restrict delete — the ledger outlives everything.
2. **`Cart.AppliedPercentOff`** `int NULL` is added. Phase 07 stores the code the customer applied; this stores what it was worth when they applied it, so order placement can tell whether the coupon has since become worth *less*. One nullable column, and without it the rule below is undetectable. Set alongside `CouponCode`; cleared with it.

### Migration

```bash
dotnet ef migrations add AddOrders \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

One new table plus two owned-type tables (or owned collections in `Orders`, depending on configuration), one FK, one column. Review the cascade behaviour on the owned collections — deleting an order must take its lines and events, and nothing else.

---

## API Contract

`OrdersController : ApiControllerBase`, class-level `[Authorize]`.

### `POST /orders`

| | |
|---|---|
| Auth | `[Authorize]` |
| Headers | `Idempotency-Key` — optional but strongly recommended |
| Request | `PlaceOrderRequest` |
| Success | `201` `OrderDto` + `Location: /api/v1/orders/{id}` |
| Errors | `401`, `409` stock or cart mismatch, `422` validation or coupon |

```jsonc
{
  "lines": [{ "productId": "0198c4…", "quantity": 2 }],
  "shippingAddress": {
    "fullName": "…", "line1": "…", "line2": null,
    "city": "…", "postalCode": "…", "country": "…", "phone": "…"
  },
  "shippingMethod": "standard",
  "couponCode": "MANGA10"
}
```

**Ids and quantities only. The server decides every price.** There is no field in this request for a unit price, a subtotal, a discount amount or a total, and none may be added.

> **`productId`, not `mangaId`.** The guideline's `PlaceOrderRequest` example still says `mangaId`; the frontend's `PlaceOrderRequest` and `OrderLine` both use `productId`, and `checkout.page.ts` submits `{ productId, quantity }`. The TypeScript is the wire truth — an order keyed on `mangaId` could not record a gift-card purchase. Phase 06 carries the same correction.

`couponCode` is a code, not an amount — re-validated and re-priced here.

### `GET /orders`

| | |
|---|---|
| Request | `pageNumber`, `pageSize` |
| Success | `200` `PaginatedList<OrderDto>` |
| Errors | `401` |

The caller's own orders, newest first. Scoped by `ICurrentUser.Id`; there is no parameter to widen it.

### `GET /orders/{id}`

| | |
|---|---|
| Success | `200` `OrderDto` |
| Errors | `401`, `404` |

> **Return 404, not 403, for someone else's order.** A 403 confirms the order exists, which leaks whether a given id is real. The response for an order that belongs to another customer must be byte-identical to the response for an id that was never issued.

### `OrderDto`

```jsonc
{
  "id": "0198c4…",
  "reference": "MS-2A7F41",
  "placedOn": "2026-08-21T14:03:11.482Z",
  "status": "pending",
  "history": [{ "status": "pending", "occurredAt": "2026-08-21T14:03:11.482Z" }],
  "lines": [
    { "productId": "0198c4…", "slug": "ashfall-ronin", "title": "Ashfall Ronin",
      "unitPrice": 12.99, "quantity": 2 }
  ],
  "shippingAddress": { "fullName": "…", "line1": "…", "line2": null,
                       "city": "…", "postalCode": "…", "country": "…", "phone": "…" },
  "shippingMethod": "standard",
  "subtotal": 25.98, "discount": 2.60, "couponCode": "MANGA10",
  "shipping": 4.99, "tax": 3.27, "total": 31.64, "currency": "USD"
}
```

Field for field, this is the client's `Order` interface in `models/order.model.ts`. Note `placedOn`, not `placedAt` — the entity column and the DTO member differ, and the DTO wins.

`lines[].title` is a plain string, not a localized object: it is a snapshot.

---

## Business Rules

### The placement transaction

Everything below happens inside **one** explicit transaction. `IUnitOfWork` only exposes `SaveChangesAsync`, so the service opens the transaction on the `DbContext` through a small Infrastructure seam — or `IUnitOfWork` gains `BeginTransactionAsync`. Prefer the latter: it keeps the Application layer free of EF, and it is a two-line addition to an interface that already owns commit semantics.

1. **Validate the request** through `IValidationService`. Short-circuit on failure.
2. **Check the idempotency key.** A prior order for `(userId, key)` → return it and stop. No transaction needed.
3. **Load the caller's cart** with products, tracked.
4. **Reconcile the request against the cart.** See below.
5. **Begin the transaction.**
6. **Reprice every line** from the current `Product.Price` and `Currency`. Never from the request.
7. **Re-validate the coupon** through `ICouponService.ValidateForCartAsync`.
8. **Compute totals** with the same `ICartPricingCalculator` the cart uses.
9. **Reserve stock** through `IInventoryService.ReserveAsync`, which writes the ledger rows.
10. **Write the order**, its lines, and a `Pending` status event.
11. **Increment `Coupon.TimesUsed` and write `CouponRedemption`**, if a coupon applied.
12. **Clear the cart**, including its coupon.
13. **Commit.**
14. Return `201` with `Location`.

Any failure between 5 and 13 rolls back everything. A customer whose order failed on stock must find their cart exactly as they left it.

> Steps 9 and 6 both touch products. `ReserveAsync` uses `ExecuteUpdateAsync`, which **executes immediately rather than at `SaveChangesAsync`** — so it must run inside the transaction opened at step 5, not before it. Phase 05 says the same thing from the other side; getting it wrong means stock moves for an order that then rolls back.

### Reconciling the request against the cart

The request carries lines; the server owns a cart. If they disagree, the customer is looking at a stale page.

Compare the submitted `(productId, quantity)` set with the cart's. On any difference — a product missing, an extra one, a different quantity — fail with **409** and title `Cart.Conflict`: "Your cart has changed. Please review it and try again."

The alternative is to ignore the submitted lines and order the cart as it stands. That is simpler and it is wrong: it charges the customer for a basket they were not shown. The customer agreed to a total, and the lines behind that total are part of the agreement.

> If the frontend ever stops sending lines, this check becomes a no-op rather than a problem. It costs one set comparison.

### Repricing

Every line's `UnitPrice` comes from the live `Product.Price` at this instant. If the price has risen since the cart was displayed, the order is placed at the **new** price — the same way the cart would have shown it on a refresh.

That is defensible for a price change and indefensible for a coupon change, which is why the coupon has the stricter rule below. A price is a property of the product and the customer sees it on every page; a coupon's value is invisible until it is applied.

All lines must share a currency. Mixed currencies in one cart is a data error, not a supported case — fail with 422 rather than picking one.

### Coupon re-validation — the strict rule

`couponCode` is re-validated against the repriced lines.

- **No longer valid** — expired, deactivated, limit reached, minimum no longer met, product removed — **fail the order with the coupon's own error** (422, `Coupon.*`). Do not quietly drop the discount and charge more: charging more than the cart displayed is worse than refusing the order.
- **Still valid but now worth less than `Cart.AppliedPercentOff`** — fail with `Coupon.NotApplicable`. The customer agreed to a total, not to a code.
- **Still valid and worth the same or more** — apply it.

The client cannot detect either case itself: it submits a code and has no way to state what it expected the code to be worth without also becoming a source of prices, which is the thing this contract exists to prevent. `Cart.AppliedPercentOff`, added by this phase's migration, is what makes the second rule detectable at all.

On rejection the client's `checkout.page.ts` already handles it: `if (error.code?.startsWith('Coupon.'))` it removes the coupon from the cart so the retry can succeed.

### Stock

`IInventoryService.ReserveAsync` does the work, atomically, with lines locked in `ProductId` order. Insufficient stock is **409** with the title named in `detail`, so the client can point at the right row:

```text
Only 1 of "Ashfall Ronin" remains; you asked for 2.
```

An inactive or deleted product fails as unavailable — a distinct message from a stock shortfall, because the fixes differ.

`Unlimited` lines reserve nothing and produce no ledger row.

### Status begins and stays at `Pending`

**A new order is `Pending`. Nothing in this phase advances it.**

`InMemoryOrderService` sets `paid` immediately with history `[pending, paid]`. That is a mock filling in for a gateway. Copying it would mean the backend asserting that money moved, which is exactly what the brief forbids: *do not implement fake payment processing.*

`Pending → Paid` happens in Phase 11, driven by a real payment confirmation. Until then every order sits at `Pending`, and that is the honest state.

The client renders `orders.status.pending` — "Awaiting payment" — which is true.

### Reference generation

`MS-` plus six characters from an unambiguous alphabet (Crockford base32: no `I`, `L`, `O`, `U`), drawn from `RandomNumberGenerator`. Roughly a billion values; collisions are rare and the unique index catches them. Retry up to three times, then fail with 500 — three collisions in a row means something is wrong with the generator, not with the customer.

Human-facing and quoted in support conversations, so it must be readable aloud. Not derived from the id, not sequential — a sequential reference tells every customer how many orders the shop has taken.

### Idempotency

Checkout is the one place a double submit is expensive.

`Idempotency-Key` header, stored on the order, unique per `(UserId, IdempotencyKey)`. A repeat key returns **the original order** with `200` and a `Location` header — `200` rather than `201` because nothing new was created, and the client cannot tell the difference in any way that matters.

The key is scoped **per user**. A global unique index would let one customer's key collide with another's and return them someone else's order, which is a data leak dressed as a cache hit.

A key that arrives while the first request is still in flight hits the unique index and fails. Catch `DbUpdateException`, re-read by key, and return the winner. This is the double-click case and it is the whole point.

Requests with no key are not deduplicated. Recommend it in the OpenAPI description; do not require it, because the frontend does not send one yet.

Cap the key at 80 characters and validate it as printable ASCII — it becomes an indexed column.

### Ownership

`GET /orders` filters by `ICurrentUser.Id`. `GET /orders/{id}` filters by **both** id and user id in the same query, so a mismatch produces "not found" naturally rather than through a separate check someone could later reorder.

```csharp
var order = await _orders.GetForUserAsync(id, _currentUser.Id!.Value, ct);
if (order is null)
{
    return ResultError.NotFound<Order>(id);   // 404, whether it is missing or someone else's
}
```

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Class-level `[Authorize]`. |
| Authorization | Ownership enforced in the query predicate, not in a branch after the read. |
| Role checks | None. Admin order management is Phase 13. |
| Validation | `PlaceOrderRequestValidator` — at least one line, at most 50; quantity 1 to `MaxLineQuantity`; every address field present and length-bounded; phone matching `^[+0-9 ()-]{6,20}$` (the client's own rule); `shippingMethod` a known value; `couponCode` at most 40 characters. |
| Sensitive data | An order carries a shipping address and a phone number. It must never appear in a log line, an error `detail`, or another customer's response. `GlobalExceptionHandler` already suppresses exception details outside Development — verify no order field reaches a Serilog message. |
| Concurrency | Stock via Phase 05's guarded update; coupon redemption via a unique index; double submit via the idempotency index; the whole thing inside one transaction. |
| Rate limiting | The global `fixed` policy. Idempotency, not throttling, is the right defence against a double submit. |

### Threats worth naming

- **Price tampering.** Structurally impossible: no price field exists in the request.
- **Discount tampering.** Same — a code, never an amount.
- **Order enumeration.** Defeated by 404-for-everything and by v7 GUIDs, which are unguessable even though they are time-ordered.
- **Reference enumeration.** `MS-` references are random, not sequential, and are not accepted as a lookup key by any endpoint in this phase.
- **Coupon farming via repeated placement.** `CouponRedemption`'s unique `(CouponId, UserId)` index makes a second redemption impossible even under a race.
- **Cross-user idempotency collision.** Prevented by scoping the key to the user.

### PII and the address

The shipping address is the only personal data the shop stores beyond an email and a display name. It is written once and read only by its owner. Two things follow: do not add an endpoint that lists addresses across orders, and if a data-deletion request is ever implemented, this is the table it has to reach.

---

## Frontend Contract

| Frontend method | Endpoint |
|---|---|
| `OrderService.place(request)` | `POST /orders` |
| `OrderService.list(page, size)` | `GET /orders` |
| `OrderService.getById(id)` | `GET /orders/{id}` |

Swap `{ provide: OrderService, useClass: HttpOrderService }` in `app.config.ts`. `/account/orders` and `/account/orders/:id` go live immediately.

**Do not wire up the checkout button.** `CartPage.checkout()` stays a toast until Phase 11. The endpoint exists and is tested; nothing in the UI reaches it. That is the deliberate state described in the guideline, and un-deliberating it here would put a payment form in front of customers with no cashier behind it.

Three things that will look different from the mock:

1. **`status` is `pending`, not `paid`.** The order detail timeline shows one event, not two. `orders.status.pending` is already translated.
2. **`title` on a line is a snapshot in one language.** Switching to Arabic will not re-translate a past order. Correct, and worth a note in the UI if anyone asks.
3. **Placement can fail with 409.** Two shapes: `Product.Conflict` (a line is short) and `Cart.Conflict` (the cart changed). The checkout page handles coupon errors already; these two need their own sentences in `ErrorMessageService` and `i18n/{en,ar}.json`.

`Idempotency-Key` is worth adding to `HttpOrderService` when the checkout is wired: a UUID generated when the review step is entered, reused across retries of the same attempt.

---

## Testing

### Unit tests (`MangaStore.UnitTests`)

| Test | Asserts |
|---|---|
| `OrderServiceTests.Place_PricesFromProductNotFromRequest` | A tampered request price is ignored; the order uses `Product.Price`. |
| `OrderServiceTests.Place_SnapshotsTitleAndUnitPrice` | Change the product afterwards; the order is unchanged. |
| `OrderServiceTests.Place_StatusIsPending` | **Not `paid`.** History has exactly one event. |
| `OrderServiceTests.Place_ClearsCartIncludingCoupon` | |
| `OrderServiceTests.Place_CartMismatch_Returns409CartConflict` | Quantity differs between request and cart. |
| `OrderServiceTests.Place_InsufficientStock_Returns409NamingTitle` | `detail` contains the title, requested and available. |
| `OrderServiceTests.Place_ExpiredCoupon_FailsWithCouponError` | 422 `Coupon.*`, **not** a silent drop. |
| `OrderServiceTests.Place_CouponNowWorthLess_Returns422NotApplicable` | Applied at 20%, now 10% → refused. **The rule `Cart.AppliedPercentOff` exists for.** |
| `OrderServiceTests.Place_CouponNowWorthMore_Succeeds` | The asymmetry is deliberate. |
| `OrderServiceTests.Place_MixedCurrencies_Returns422` | |
| `OrderServiceTests.Place_RepeatIdempotencyKey_ReturnsOriginalOrder` | Same id, same reference, one order in the repository. |
| `OrderServiceTests.Place_SameKeyDifferentUser_CreatesSeparateOrders` | The cross-user leak. |
| `OrderServiceTests.Place_NoIdempotencyKey_CreatesTwoOrders` | Not deduplicated without a key. |
| `OrderServiceTests.Place_StockFailure_RollsBackEverything` | No order, no ledger row, no redemption, cart intact. |
| `OrderServiceTests.Place_IncrementsTimesUsedAndWritesRedemption` | Once. |
| `OrderServiceTests.GetById_OtherUsersOrder_Returns404` | Not 403. |
| `OrderServiceTests.List_ReturnsOnlyCallersOrders` | |
| `ReferenceGeneratorTests.ProducesUnambiguousAlphabet` | No `I`, `L`, `O`, `U`; matches `^MS-[0-9A-HJ-NP-TV-Z]{6}$`. |
| `ReferenceGeneratorTests.CollisionRetriesThenFails` | Three collisions → failure, not an infinite loop. |

### Integration tests (`MangaStore.IntegrationTests`)

| Test | Asserts |
|---|---|
| `OrderApiTests.Place_Returns201WithLocation` | Header points at `GET /orders/{id}` and that URL resolves. |
| `OrderApiTests.Place_ResponseMatchesFrontendOrderInterface` | Every member of `models/order.model.ts` present, `placedOn` not `placedAt`. |
| `OrderApiTests.Place_StatusSerializesAsPending` | Raw JSON `"status":"pending"`. |
| `OrderApiTests.RepeatIdempotencyKey_Returns200NotDuplicate` | And `GET /orders` shows one. |
| `OrderApiTests.GetById_OtherUsersOrder_IsByteIdenticalToUnknownId` | Compare both bodies. **The leak test.** |
| `OrderApiTests.List_IsPaginatedNewestFirst` | Envelope plus ordering. |
| `OrderApiTests.AllEndpoints_RequireAuthentication` | |
| `OrderApiTests.Place_PersistsInsideOneTransaction` | Force a failure at step 11 with a deliberately invalid coupon state; assert no order row survives. |
| `OrderApiTests.Place_ThenCancelViaInventory_RestoresStock` | Cross-phase, using Phase 05's `ReleaseAsync` directly since no cancel endpoint exists yet. |

### The concurrency test

As in Phase 05, and for the same reason: **SQLite proves nothing here.** Mark it `[Trait("Category", "SqlServer")]`:

```csharp
public async Task Place_TenConcurrentOrders_ForFiveUnits_SucceedsExactlyFiveTimes()
```

Ten users, one unit each, five in stock. Assert exactly five `201`s, five `409`s, five orders, five ledger rows, and a final stock of zero. Run the same shape for a single-use coupon: ten users, `UsageLimit = 1`, exactly one success.

### Edge cases

- Empty `lines`: 422.
- Quantity 0 in a line: 422 — the cart's "0 means remove" rule does not apply to an order.
- 51 lines: 422.
- A product soft-deleted between cart and checkout: 409 unavailable.
- A coupon deactivated between cart and checkout: 422 with its own error.
- `Idempotency-Key` of 81 characters: 422.
- Two identical requests with the same key arriving simultaneously: one wins, the other re-reads and returns the same order. Both callers see the same order id.
- An order whose lines are all `Unlimited`: no ledger rows, order places normally, and cancelling it later restores nothing.
- `line2` null: valid.
- A cart that became empty between the client's read and the submit: `Cart.Conflict`, not "empty lines".

---

## Acceptance Criteria

- [ ] `Order`, `OrderLine` (owned), `OrderStatusEvent` (owned), `Address` (owned type) with configurations, repository and `DbSet`; migration `AddOrders`.
- [ ] `StockMovement.OrderId` foreign key added; `Cart.AppliedPercentOff` column added.
- [ ] Unique filtered indexes on `Reference` and `(UserId, IdempotencyKey)`.
- [ ] `OrdersController` with three actions, class-level `[Authorize]`, each action one line.
- [ ] `PlaceOrderRequest` contains **no** price, subtotal, discount or total field.
- [ ] Every line repriced from `Product.Price`; `Title`, `Slug` and `UnitPrice` snapshotted.
- [ ] `OrderLine.ProductId` has no foreign key.
- [ ] Placement runs inside one explicit transaction covering repricing, coupon re-validation, stock reservation, order write, redemption and cart clearing.
- [ ] **New orders are `Pending`.** Nothing sets `Paid`.
- [ ] Coupon no longer valid → fail with the coupon's own 422 error. Coupon now worth less → 422 `Coupon.NotApplicable`. Worth more → succeed.
- [ ] Request lines reconciled against the server cart; mismatch is 409 `Cart.Conflict`.
- [ ] Insufficient stock is 409 with the product title in `detail`.
- [ ] `Idempotency-Key` honoured, scoped per user, repeat returning 200 with the original order; an in-flight duplicate re-reads rather than failing.
- [ ] `Reference` matches `^MS-[0-9A-HJ-NP-TV-Z]{6}$`, random, unique, retried on collision.
- [ ] `GET /orders/{id}` returns a byte-identical 404 for someone else's order and for an unknown id.
- [ ] `OrderDto` matches `models/order.model.ts` field for field, including `placedOn`.
- [ ] **The SQL Server concurrency test passes**: ten concurrent orders for five units yield exactly five successes; a single-use coupon under ten concurrent orders is redeemed exactly once.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.
- [ ] `CartPage.checkout()` is **not** wired up. The PR says so explicitly.

---

## Dependencies

```text
Depends on:
  Phase 01 - Money.Round, CommerceOptions, UTC timestamps.
  Phase 02 - Product.
  Phase 05 - IInventoryService.ReserveAsync/ReleaseAsync and StockMovement.
  Phase 06 - Cart and ICartPricingCalculator.
  Phase 07 - ICouponService.ValidateForCartAsync, Coupon, CouponRedemption.

Blocks:
  Phase 09 (gift-card fulfilment) - allocation hooks into placement and payment.
  Phase 11 (payment preparation)  - drives Pending -> Paid.
  Phase 12 (dashboard)            - order counts and revenue.
  Phase 13 (admin CRUD)           - PUT /orders/{id}/status.

Can be implemented independently:
  No - this is the most dependent phase in the plan. It is the point at
  which catalogue, inventory, cart and coupons have to agree.
```
