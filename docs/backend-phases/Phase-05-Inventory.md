# Phase 05 — Inventory and Stock Integrity

**Recommended branch:** `phase-05-inventory`

---

## Objective

Make stock changes safe. Phase 02 gave products stock columns; this phase gives them rules — a decrement that cannot oversell under concurrency, a ledger that records every movement and why, and query methods for low and out-of-stock reporting.

No customer-facing endpoint ships here. This is the machinery Phase 08 uses to place an order without selling the same last copy twice.

---

## Current State

### What exists after Phase 02

On `Product`: `InventoryMode` (`Tracked` / `Unlimited`), `StockQuantity`, `IsActive`, `RowVersion` (mapped `.IsRowVersion()`, unused), a `StockQuantity >= 0` check constraint, and `StockStatus.Derive`.

### What exists after Phase 03

Public queries filter `IsActive = true` and project the derived `StockStatus`. The catalogue's `inStockOnly` filter excludes `outOfStock` only.

### What is missing

Everything that writes. There is no way to decrement stock, no record of why a number changed, no protection against two orders reading the same quantity and both deciding there is enough, and no way to ask which products are running low.

`AppDbContext` is `NoTrackingWithIdentityResolution` globally, which makes a naive read-modify-write not merely racy but silently inert.

---

## Scope

| Component | Files |
|---|---|
| Domain | `StockMovement`, `StockMovementReason`, `IStockMovementRepository`; stock query and mutation methods on `IProductRepository` |
| Application | `Features/Inventory/` — `IInventoryService` / `InventoryService`, `StockAdjustmentRequest` + validator, `InsufficientStockDetail` |
| Infrastructure | `StockMovementConfiguration`, `StockMovementRepository`, the atomic decrement on `ProductRepository`, migration |

### Out of scope

- **Endpoints.** Phase 13 exposes the admin adjustment endpoint; Phase 12 consumes the counts. Nothing here is reachable over HTTP.
- **Order placement.** Phase 08 calls into this; it does not live here.
- **Gift-card code allocation.** Phase 09. A gift card's `StockQuantity` and its pool of codes are two different counters and this phase owns only the first.

---

## Database Changes

### `StockMovement` — the ledger

Every change to `StockQuantity` writes a row. Without it, "why is this product out of stock?" is unanswerable, and cancellation cannot tell whether it has already restored stock once.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `ProductId` | `uniqueidentifier` | FK, restrict delete — the ledger outlives a soft-deleted product |
| `Delta` | `int` | Negative for a sale, positive for a restock or cancellation. Never zero |
| `QuantityAfter` | `int` | The resulting `StockQuantity`. Makes the ledger readable without replaying it |
| `Reason` | `int` | `StockMovementReason` |
| `OrderId` | `uniqueidentifier` NULL | Set for `OrderPlaced` and `OrderCancelled`. FK added in Phase 08 |
| `Note` | `nvarchar(400)` NULL | Free text for `Adjustment` and `Restock` |
| `PerformedByUserId` | `uniqueidentifier` NULL | The admin, for `Adjustment` and `Restock`. Null for system-driven movements |

```csharp
/// <summary>Why a product's stock changed.</summary>
public enum StockMovementReason
{
    /// <summary>An administrator set the level directly.</summary>
    Adjustment,

    /// <summary>Units arrived.</summary>
    Restock,

    /// <summary>Units were committed to an order.</summary>
    OrderPlaced,

    /// <summary>An order was cancelled and its units returned.</summary>
    OrderCancelled,
}
```

Indexes: `(ProductId, CreatedAt DESC)` for a product's history; **unique filtered on `(OrderId, Reason)` where `OrderId IS NOT NULL`**. That second one is the idempotency guarantee — it makes a double cancellation a constraint violation rather than a double restock.

`HasQueryFilter(m => !m.IsDeleted)` as on every entity, though nothing should ever soft-delete a ledger row.

> The `OrderId` foreign key cannot be created until Phase 08 defines `Order`. Add the column and the index now, and the FK constraint in Phase 08's migration. A nullable `Guid` with no constraint is honest about what exists today; a fabricated `Orders` table would not be.

### Migration

```bash
dotnet ef migrations add AddStockMovements \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

One new table. Confirm the filtered unique index carries `WHERE [OrderId] IS NOT NULL AND [IsDeleted] = 0`.

---

## API Contract

**None.** No controller, no route.

The service surface other phases call:

```csharp
/// <summary>Stock reservation and adjustment. All operations are atomic per product.</summary>
public interface IInventoryService
{
    /// <summary>Commits stock for an order, or fails naming every line that cannot be satisfied.</summary>
    Task<Result> ReserveAsync(
        IReadOnlyList<StockReservation> reservations, Guid orderId, CancellationToken ct = default);

    /// <summary>Returns stock committed to an order. Safe to call more than once.</summary>
    Task<Result> ReleaseAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Sets a product's stock to an absolute level and records why.</summary>
    Task<Result<StockLevelDto>> AdjustAsync(
        Guid productId, StockAdjustmentRequest request, CancellationToken ct = default);

    /// <summary>Products at or below the low-stock threshold, most depleted first.</summary>
    Task<Result<PaginatedList<StockLevelDto>>> GetLowStockAsync(
        PaginationParams pagination, CancellationToken ct = default);

    /// <summary>Products with no stock, most recently depleted first.</summary>
    Task<Result<PaginatedList<StockLevelDto>>> GetOutOfStockAsync(
        PaginationParams pagination, CancellationToken ct = default);
}
```

`StockReservation` is `(Guid ProductId, int Quantity)`. `StockLevelDto` is `(Guid ProductId, string Slug, string Title, InventoryMode Mode, int StockQuantity, StockStatus Status, bool IsActive)`.

---

## Business Rules

### The decrement is one statement, not a read-modify-write

The obvious implementation is wrong:

```csharp
// WRONG. Two concurrent orders both read 1, both decide 1 >= 1, both write 0.
// The shop has sold two copies of its last one.
var product = await _repo.GetByIdAsync(id, ct);
if (product.StockQuantity < quantity) return ResultError.Conflict(...);
product.StockQuantity -= quantity;
await _unitOfWork.SaveChangesAsync(ct);
```

Do it as a guarded update and let the database arbitrate:

```csharp
int affected = await _context.Products
    .Where(p => p.Id == productId
             && p.InventoryMode == InventoryMode.Tracked
             && p.StockQuantity >= quantity)
    .ExecuteUpdateAsync(
        s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
              .SetProperty(p => p.UpdatedAt, _dateTime.UtcNow),
        ct);

// affected == 0 means the guard failed: not enough stock, wrong mode, or gone.
```

The `WHERE` clause and the `SET` evaluate inside a single statement holding a row lock. Two concurrent callers serialise; the second sees the first's write and its guard fails. No retry loop, no `RowVersion` round trip, no window.

Three consequences to write down, because each one has bitten someone:

1. **`ExecuteUpdateAsync` bypasses the change tracker**, so `AuditInterceptor` never runs. `UpdatedAt` must be set explicitly in the `SetProperty` chain, as above.
2. **It executes immediately**, not at `SaveChangesAsync`. To be atomic with the order insert it must run inside an explicit transaction that Phase 08 opens.
3. **It respects query filters**, so a soft-deleted product is already excluded. Good — but it means `affected == 0` has several possible causes and the error message has to distinguish them with a follow-up read.

### `RowVersion` is for editors, not for stock

`Product.RowVersion` stays and is used by Phase 13's admin update: two admins editing the same product should get a 409 rather than last-write-wins. It is deliberately **not** used for stock, because optimistic concurrency on a hot row means a retry loop under exactly the load where you least want one.

Two mechanisms, two jobs. Say so in the code, or someone will "simplify" one into the other.

### `Unlimited` skips the decrement entirely

A product in `InventoryMode.Unlimited` has no meaningful `StockQuantity` and no reservation to make. `ReserveAsync` skips it — no `ExecuteUpdate`, no ledger row. Writing a movement for a counter nobody reads would fill the ledger with noise.

`StockStatus.Derive` already returns `InStock` for `Unlimited` regardless of quantity.

### Reserving a multi-line order

`ReserveAsync` takes every line at once and must be all-or-nothing.

1. Open a transaction (or join Phase 08's).
2. **Order the lines by `ProductId`.** Two orders containing the same two products in opposite order will deadlock otherwise, each holding one row and waiting for the other. A consistent lock order removes the cycle. This is cheap and it is the kind of bug that only appears under load.
3. Decrement each `Tracked` line with the guarded update.
4. If any returns `affected == 0`, roll back and fail.
5. Write one `StockMovement` per decremented line, `Reason = OrderPlaced`, `OrderId` set.
6. Commit.

The failure carries **which line failed and by how much**, because the client points at the row:

```csharp
return ResultError.Conflict(
    "Product",
    $"Only {available} of \"{title}\" remain; you asked for {requested}.");
```

`ResultError.Conflict` maps to **409**, which is what the guideline specifies for `POST /orders` and what the checkout page expects.

### Releasing stock — the exact cancellation rule

The brief asks for this rule to be documented precisely, so:

> **Stock is returned when, and only when, an order that decremented it moves to `Cancelled`.**
>
> - Decrement happens once, at order placement (`Pending`).
> - Movement through `Paid`, `Shipped` and `Delivered` changes nothing — the units are already committed and were never un-committed.
> - `Cancelled` returns exactly the quantities that were taken, read from the `OrderPlaced` ledger rows for that order, **not** recomputed from the order's current lines.
> - Cancelling an already-cancelled order returns nothing and succeeds.

Reading the restore quantities from the ledger rather than from the order lines matters: it is the only source that says what was actually decremented. A `Unlimited` line has no `OrderPlaced` row, so it contributes nothing to the restore — which is correct, and would be wrong if the code iterated the order's lines instead.

Idempotency is enforced by the unique filtered index on `(OrderId, Reason)`. A second `ReleaseAsync` for the same order violates it; catch, log, return `Result.Ok()`. The desired state was already true.

> There is no partial cancellation and no refund flow in this design, because neither exists in the frontend. If per-line cancellation is wanted later, the ledger already carries `ProductId` per row and can support it without a schema change.

### Adjustment sets an absolute level

`AdjustAsync` takes the level the admin wants, not a delta:

```csharp
/// <summary>Sets a product's stock to an absolute level.</summary>
/// <param name="Quantity">The resulting stock level. Must not be negative.</param>
/// <param name="Reason">Why the level changed.</param>
/// <param name="Note">Free-text explanation, shown in the movement history.</param>
public sealed record StockAdjustmentRequest(int Quantity, StockMovementReason Reason, string? Note);
```

An absolute level is what a stock count produces and what an admin form shows. A delta form makes a double-submitted `+50` into `+100`; an absolute form makes it a no-op.

Rules: `Quantity >= 0` (validator, plus the check constraint). `Reason` must be `Adjustment` or `Restock` — an admin cannot forge an `OrderPlaced` row. `PerformedByUserId` comes from `ICurrentUser.Id`, never from the request. The ledger records `Delta = newQuantity - oldQuantity`; if that is zero, write nothing and succeed.

Adjustment is the one place a read-modify-write is acceptable, because it needs the old value for the ledger and it is a single-admin operation. Use `RowVersion` here so two admins adjusting at once get a 409.

### `IsActive` is not touched by any of this

Nothing in this phase changes `IsActive`. Selling out does not withdraw a product, and withdrawing one does not zero its stock. A product with `IsActive = false` and `StockQuantity = 20` is the exact case the brief names, and it must survive an order, a cancellation and an adjustment unchanged.

Reserving stock for an **inactive** product must fail: the guard adds `p.IsActive`. Phase 08 checks it explicitly too, so the customer gets "no longer available" rather than a bare stock conflict.

### Low and out-of-stock queries

Both are `Tracked` products only — an `Unlimited` product is never low. Both include inactive products, because an admin reviewing stock wants to see everything; the public catalogue never calls these.

| Query | Predicate | Order |
|---|---|---|
| Low stock | `Tracked && StockQuantity > 0 && StockQuantity <= LowStockThreshold` | `StockQuantity` ascending |
| Out of stock | `Tracked && StockQuantity == 0` | `UpdatedAt` descending |

Threshold from `CommerceOptions.LowStockThreshold`, so it is one number, configured once, shared with `StockStatus.Derive`.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | No endpoint. `AdjustAsync` reads `ICurrentUser.Id` for the ledger and returns `ResultError.Unauthorized` if there is none — a stock adjustment with no attributable actor should not happen. |
| Authorization | Enforced at the endpoint in Phase 13 (`[Authorize(Roles = Roles.Admin)]`). `InventoryService` does not check roles itself — that is the controller's job and duplicating it in two places means one of them will drift. |
| Validation | `StockAdjustmentRequestValidator`: `Quantity >= 0`, `Reason` in `{Adjustment, Restock}`, `Note` at most 400 characters. |
| Sensitive data | Stock levels are not secret. The **ledger** is more sensitive than the levels: it reveals sales volume per product. Never expose `StockMovement` through a public endpoint. |
| Concurrency | The whole point of the phase. Guarded `ExecuteUpdate` for stock; `RowVersion` for admin edits; ordered locking to avoid deadlocks; a unique index for release idempotency. |
| Rate limiting | Not applicable — no endpoint. Phase 13 puts the admin endpoints behind the global limiter. |

### Attack shapes worth naming

- **Oversell by racing checkout.** Two simultaneous `POST /orders` for the last unit. Defeated by the guarded update.
- **Stock probing.** Repeatedly adding to the cart to binary-search the exact stock level. Not fully preventable — the shop has to say when something is unavailable — but do not return exact remaining quantities to anonymous callers. The 409 detail names the shortfall only at order placement, to a signed-in customer who is actually buying.
- **Negative-quantity adjustment.** Blocked by the validator and the check constraint, in that order.

---

## Frontend Contract

**Nothing new is consumed.** The storefront has no inventory UI and no admin area — `isActive` and stock levels appear nowhere in `manga-store\src`.

What the frontend does consume, indirectly:

- `stockStatus` on every product DTO, which stays correct as stock moves.
- A **409** from `POST /orders` when a line cannot be satisfied, with the title named in `detail`. `checkout.page.ts` renders that inline against the offending row.

The one thing this phase must not do is change the public shape of `stockStatus`. A card showing "In stock" for something that cannot be bought is worse than showing nothing.

---

## Testing

### Unit tests (`MangaStore.UnitTests`)

| Test | Asserts |
|---|---|
| `InventoryServiceTests.Reserve_UnlimitedProduct_WritesNoMovement` | No ledger row, no decrement, success. |
| `InventoryServiceTests.Reserve_InsufficientStock_FailsNamingTheProduct` | `ResultError.Conflict`, and `detail` contains the title, the requested quantity and the available quantity. |
| `InventoryServiceTests.Reserve_InactiveProduct_Fails` | Even with ample stock. |
| `InventoryServiceTests.Reserve_MultiLine_IsAllOrNothing` | Line 1 succeeds, line 2 fails → line 1 is rolled back and no movement survives. |
| `InventoryServiceTests.Reserve_OrdersLinesByProductId` | The deadlock-avoidance ordering, asserted on the call sequence. |
| `InventoryServiceTests.Release_UsesLedgerQuantitiesNotOrderLines` | Ledger says 2, the order's lines now say 5 → 2 is restored. |
| `InventoryServiceTests.Release_Twice_SucceedsAndRestoresOnce` | The idempotency contract. |
| `InventoryServiceTests.Release_OrderWithNoMovements_Succeeds` | An all-`Unlimited` order cancels cleanly. |
| `InventoryServiceTests.Adjust_SetsAbsoluteLevel` | From 20 to 5 writes `Delta = -15`, `QuantityAfter = 5`. |
| `InventoryServiceTests.Adjust_ToSameLevel_WritesNoMovement` | And still succeeds. |
| `InventoryServiceTests.Adjust_OrderPlacedReason_Is422` | An admin cannot forge a sale. |
| `InventoryServiceTests.Adjust_Anonymous_IsUnauthorized` | No `ICurrentUser.Id`, no adjustment. |
| `InventoryServiceTests.Adjust_DoesNotChangeIsActive` | The separation, pinned. |

### Integration tests (`MangaStore.IntegrationTests`)

| Test | Asserts |
|---|---|
| `InventoryTests.ReserveThenRelease_ReturnsToOriginalLevel` | Round trip through the real `DbContext`. |
| `InventoryTests.LowStockQuery_ExcludesUnlimitedAndZero` | Boundaries: `StockQuantity == threshold` is low; `threshold + 1` is not; `0` is out, not low. |
| `InventoryTests.OutOfStockQuery_IncludesInactiveProducts` | Admin views see everything. |
| `InventoryTests.NegativeStock_IsRejectedByConstraint` | Belt and braces with the validator. |
| `InventoryTests.DuplicateReleaseMovement_ViolatesUniqueIndex` | The index is really there and really filtered. |

### The concurrency test — and its honest limitation

> **`CustomWebApplicationFactory` runs SQLite in-memory, and SQLite does not implement `rowversion` and serialises writes very differently from SQL Server.** A green SQLite suite proves nothing about oversell. Do not let it.

Cover it two ways:

1. **A service-level unit test** with a substituted repository whose guarded-decrement stub returns `affected = 0` on the second call. This proves the *service* handles the loss correctly — it does not prove the SQL is atomic.
2. **A SQL Server integration test**, marked with a trait so it can be excluded locally and run in CI where a SQL Server container exists:

```csharp
[Trait("Category", "SqlServer")]
public async Task Reserve_TwentyConcurrentCallers_ForTenUnits_SucceedsExactlyTenTimes()
```

Twenty parallel `ReserveAsync` calls for one unit each, against a product with ten. Assert exactly ten successes, exactly ten `OrderPlaced` movements, and a final `StockQuantity` of zero. **This is the only test that actually proves the phase's objective**, and the acceptance criteria should not be considered met without it.

`docker-compose.yml` already defines a SQL Server 2022 service with a health check, so the container is available.

### Edge cases

- Reserve zero quantity: rejected by the validator upstream in Phase 08; `ReserveAsync` treats it as a programming error.
- Reserve for a product deleted between cart and checkout: `affected == 0` via the query filter, reported as unavailable rather than out of stock.
- Adjust a product whose `InventoryMode` is `Unlimited`: allowed — the number is stored and ignored — but the response's `StockStatus` still says `InStock`. Do not silently switch the mode.
- Release for an order id that never existed: no movements, success.
- Stock at `int.MaxValue`: not defended against. The check constraint catches the negative side, which is the one that matters.

---

## Acceptance Criteria

- [ ] `StockMovement` entity, configuration, repository and `DbSet`; one migration `AddStockMovements`.
- [ ] Unique filtered index on `(OrderId, Reason)` where `OrderId IS NOT NULL`.
- [ ] `IInventoryService` with `ReserveAsync`, `ReleaseAsync`, `AdjustAsync`, `GetLowStockAsync`, `GetOutOfStockAsync`.
- [ ] Decrement implemented as a **single guarded `ExecuteUpdateAsync`**, never a read-modify-write, with `UpdatedAt` set explicitly because the audit interceptor is bypassed.
- [ ] Multi-line reservation is all-or-nothing, inside a transaction, with lines locked in `ProductId` order.
- [ ] Insufficient stock returns `ResultError.Conflict` (→ 409) naming the product, the requested quantity and the available quantity.
- [ ] `Unlimited` products are skipped entirely — no decrement, no ledger row.
- [ ] Inactive products cannot be reserved.
- [ ] `ReleaseAsync` restores from the **ledger**, not from order lines, and is idempotent.
- [ ] `AdjustAsync` takes an absolute level, records `Delta` and `QuantityAfter`, attributes to `ICurrentUser.Id`, refuses `OrderPlaced`/`OrderCancelled` reasons, and uses `RowVersion` for admin-vs-admin conflict.
- [ ] Nothing in this phase writes `IsActive`.
- [ ] Low/out-of-stock queries use `CommerceOptions.LowStockThreshold` and cover `Tracked` products only.
- [ ] **The SQL Server concurrency test exists and passes**: 20 concurrent single-unit reservations against 10 units yield exactly 10 successes and a final quantity of 0.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.
- [ ] No controller and no route was added.

---

## Dependencies

```text
Depends on:
  Phase 01 - CommerceOptions.LowStockThreshold, IDateTime, the change-tracking warning.
  Phase 02 - Product with InventoryMode, StockQuantity, IsActive, RowVersion.

Blocks:
  Phase 08 (orders)          - hard block; order placement reserves through this.
  Phase 09 (gift-card codes) - code allocation runs alongside the stock decrement.
  Phase 12 (dashboard)       - low/out-of-stock counts come from here.
  Phase 13 (admin CRUD)      - exposes AdjustAsync over HTTP.

Can be implemented independently:
  No - requires Phases 01 and 02. It does not require Phase 03 or 04:
  nothing here reads the catalogue API or the seeded data.

  Note the forward reference: StockMovement.OrderId is a plain nullable Guid
  in this phase's migration. Phase 08 adds the foreign-key constraint once
  Orders exists.
```
