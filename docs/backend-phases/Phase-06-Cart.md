# Phase 06 — Server-Owned Cart

**Recommended branch:** `phase-06-cart`

---

## Objective

Move the cart from the browser to the server, and make the server the only thing that decides what a cart costs.

Today the cart lives in `localStorage` keyed by product id, and `pricing.model.ts` computes subtotal, discount, shipping, tax and total in the browser. That is fine while nothing is charged. It stops being fine the moment an order is placed, because a total the client computed is a total the client can change.

After this phase the client sends ids and quantities and renders whatever comes back.

---

## Current State

### Backend

No cart of any kind. No `Cart`, no `CartItem`, no pricing code, no `CommerceOptions` consumer. Phase 01 defined the constants; nothing has read them yet.

### Frontend

`manga-store\src\app\core\catalog\cart.service.ts` is an abstract class bound to `InMemoryCartService`. Its surface is **synchronous and signal-based**:

```ts
abstract readonly lines: Signal<readonly CartLine[]>;
abstract readonly count: Signal<number>;
abstract readonly totals: Signal<CartTotals>;
abstract readonly coupon: Signal<AppliedCoupon | null>;
abstract add(product: ProductSummary, quantity?: number): void;
abstract setQuantity(productId: string, quantity: number): void;
abstract remove(productId: string): void;
abstract clear(): void;
abstract applyCoupon(code: string): Observable<AppliedCoupon>;
abstract restore(): Observable<void>;
```

Storage key `mangastore.cart`, shape `{ lines: [{productId, quantity}], coupon?: string }`, with tolerance for a legacy bare array. It persists **ids and quantities only** — never prices, never a discount amount — and re-resolves products through `CatalogService` on load, which is what stops a restored cart quoting a stale price.

The stepper is clamped 1–10 (`CART_RULES.maxQuantity`); `setQuantity(id, 0)` removes the line.

### The decision this phase implements

The guideline (§5.2) recommends a server-owned cart tied to the user, and that is what was chosen. It survives a device change, it makes the server the single authority on price and stock, and it is what a coupon endpoint needs in order to judge eligibility against a real basket rather than numbers the client sent.

The cost: an anonymous visitor has no server cart. The local cart stays for signed-out browsing and merges on sign-in.

---

## Scope

| Component | Files |
|---|---|
| Domain | `Cart`, `CartItem`, `ICartRepository` |
| Application | `Features/Cart/` — `CartDto`, `CartLineDto`, `CartTotalsDto`, `AddCartItemRequest`, `UpdateCartItemRequest`, `MergeCartRequest`, validators, `CartProfile`, `ICartService` / `CartService`; `Common/Pricing/ICartPricingCalculator` + implementation |
| Infrastructure | `CartConfiguration`, `CartItemConfiguration`, `CartRepository`, migration |
| API | `CartController` |

### Out of scope

- **Coupons.** Phase 07 adds `POST /cart/coupons` and fills in `discount` and `coupon`. This phase stores `CouponCode` on the cart and always prices it as no discount, because there is no `Coupon` table to validate against yet.
- **Order placement.** Phase 08.
- **Stock reservation.** A cart is not a reservation. Phase 05 owns reservation, and it happens at checkout, not on add-to-cart.

---

## Database Changes

### `Cart`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `UserId` | `uniqueidentifier` | **Unique filtered on `IsDeleted = 0`.** One cart per user |
| `CouponCode` | `nvarchar(40)` NULL | Stored uppercase. Phase 07 validates it; this phase only remembers it |

No foreign key to `AspNetUsers`. `RefreshToken` already sets that precedent — a bare `UserId` with no navigation, because the user lives in Infrastructure and the Domain must not reference it.

### `CartItem`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `CartId` | `uniqueidentifier` | FK, cascade delete |
| `ProductId` | `uniqueidentifier` | FK, restrict delete |
| `Quantity` | `int` | 1 to `CommerceOptions.MaxLineQuantity` |

**Unique on `(CartId, ProductId)`, filtered on `IsDeleted = 0`** — adding a product twice increments rather than duplicating. Check constraint `Quantity >= 1`; quantity zero is a removal, not a state.

> The guideline models this as `CartItem(UserId, ProductId, Quantity)` with no `Cart` root. A root is used here because `CouponCode` needs somewhere to live that is not repeated on every line, and because "clear the cart" and "merge into the cart" are operations on a thing, not on a set of rows that happen to share a `UserId`.

> **`productId`, not `mangaId`.** The guideline's endpoint tables still say `mangaId` — `POST /cart/items { mangaId, quantity }`, `PUT /cart/items/{mangaId}`. That wording predates the multi-product refactor (commit `ec50eae`, "Make the catalogue multi-product, with gift cards as first-class"), and the frontend's TypeScript uses `productId` everywhere: `OrderLine.productId`, `PlaceOrderRequest.lines[].productId`, `CartService.setQuantity(productId, …)`. **The TypeScript is the wire truth.** The same correction applies to Phase 08's order lines and Phase 10's wishlist routes. A cart keyed on `mangaId` could not hold a gift card, which is half the shop.

### Migration

```bash
dotnet ef migrations add AddCart \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

Two tables. Confirm both filtered unique indexes and the quantity constraint.

---

## API Contract

`CartController : ApiControllerBase`, **class-level `[Authorize]`** — every action requires a signed-in user, and there is no anonymous action to be opened by accident. This is the opposite shape from `CatalogController` and is safe precisely because it is uniform.

Every mutation returns the **whole recalculated cart**, so the client never computes a total it might get wrong.

### `GET /cart`

| | |
|---|---|
| Auth | `[Authorize]` |
| Request | `shippingMethod` query, `standard` (default) or `express` |
| Success | `200` `CartDto` |
| Errors | `401` |

A user with no cart gets an empty one — `200` with zero lines and zero totals, never `404`. A cart is a property of a user, not a resource they may or may not have created.

`shippingMethod` is a query parameter rather than cart state because the client's checkout page previews the express surcharge before committing to it, and a preview should not mutate anything. It defaults to `standard`.

### `POST /cart/items`

| | |
|---|---|
| Request | `AddCartItemRequest { Guid ProductId, int Quantity }` |
| Success | `200` `CartDto` |
| Errors | `401`, `404` `Product.NotFound`, `409` `Product.Conflict` (insufficient stock), `422` |

Adding a product already in the cart **increments** the existing line. The resulting quantity is still capped at `MaxLineQuantity`.

`200` rather than `201`: the response is the cart, and the cart already existed. `HandleResult`, not `HandleCreated`.

### `PUT /cart/items/{productId}`

| | |
|---|---|
| Request | `UpdateCartItemRequest { int Quantity }` |
| Success | `200` `CartDto` |
| Errors | `401`, `404` `CartItem.NotFound`, `409`, `422` |

Sets an absolute quantity. **`Quantity = 0` removes the line and succeeds** — the client's stepper decrements to zero and expects the row to disappear, not an error.

### `DELETE /cart/items/{productId}`

| | |
|---|---|
| Success | `200` `CartDto` |
| Errors | `401`, `404` `CartItem.NotFound` |

Returns the cart, not `204`, so the client re-renders totals from one response instead of following up with a `GET`.

### `DELETE /cart`

| | |
|---|---|
| Success | `204` |
| Errors | `401` |

The one endpoint that returns no body, via `HandleDelete`. Nothing needs re-rendering; the cart is empty. Clearing an already-empty cart succeeds.

Also clears `CouponCode`. A coupon applied to a cart that no longer exists is not worth remembering.

### `POST /cart/merge`

| | |
|---|---|
| Request | `MergeCartRequest { IReadOnlyList<MergeCartLine> Lines }`, `MergeCartLine { Guid ProductId, int Quantity }` |
| Success | `200` `CartDto` |
| Errors | `401`, `422` (empty, or more than 50 lines) |

Called once, immediately after sign-in, with whatever the anonymous local cart held. See Business Rules for the merge rule.

### `CartDto`

```jsonc
{
  "lines": [
    { "product": { /* ProductSummaryDto, polymorphic on kind */ }, "quantity": 2 }
  ],
  "totals": {
    "subtotal": 91.98,
    "itemSavings": 6.51,
    "discount": 0,
    "coupon": null,
    "shipping": 0,
    "tax": 12.88,
    "total": 104.86,
    "currency": "USD"
  }
}
```

`CartLineDto` nests the full `ProductSummaryDto` from Phase 03 — same polymorphic shape, same already-localized strings. The client's `CartLine` is `{ product: ProductSummary, quantity: number }` and the cart page renders a product card from it, so anything less would mean a second round trip.

`coupon` stays `null` until Phase 07. `discount` stays `0`.

---

## Business Rules

### Pricing — ported exactly from `pricing.model.ts`

This is the heart of the phase. The frontend's calculator is well-reasoned and its comments record two bugs already fixed there. Port it, do not redesign it.

```csharp
/// <summary>Computes what the customer is charged. The single authority on cart totals.</summary>
public CartTotals Price(IReadOnlyList<PricedLine> lines, AppliedCoupon? coupon, ShippingMethod method)
{
    decimal subtotal = Money.Round(lines.Sum(l => l.UnitPrice * l.Quantity));

    // Informational only: already inside subtotal, never subtracted from anything.
    decimal itemSavings = Money.Round(lines.Sum(l => SavingPerUnit(l) * l.Quantity));

    decimal discount = coupon is null ? 0m : Money.Round(DiscountBase(lines, subtotal, coupon) * (coupon.PercentOff / 100m));

    // What the customer actually pays for goods, and so what tax is based on.
    decimal payable = Money.Round(subtotal - discount);

    decimal shipping = lines.Count == 0
        ? 0m
        : Money.Round(
            (subtotal >= _options.FreeShippingThreshold ? 0m : _options.ShippingFlatRate)
            + (method == ShippingMethod.Express ? _options.ExpressSurcharge : 0m));

    decimal tax = Money.Round(payable * _options.TaxRate);

    return new CartTotals(
        subtotal, itemSavings, discount, coupon, shipping, tax,
        Total: Money.Round(payable + shipping + tax),
        Currency: lines.Count > 0 ? lines[0].Currency : _options.DefaultCurrency);
}
```

Five rules that are easy to get wrong and each of which has a reason:

1. **Tax is charged on the post-discount amount.** `tax = payable * rate`, not `subtotal * rate`. Taxing the pre-discount subtotal overcharges — the customer pays tax on what they actually pay.

2. **The free-shipping threshold is judged on the *pre-discount* subtotal.** Testing it after the coupon looks more consistent and quietly lets a discount raise the bill: a $50 cart ships free at $57.00, a 5% coupon drops it to $47.50, it loses free delivery, and it comes back at $59.14. A coupon that costs the customer money is a bug however it is derived. The threshold is judged on what they put in the basket, which is also what the shop told them to spend.

3. **An empty cart charges no shipping, including no express surcharge** — a faster van for nothing is still nothing. Emptiness is the *absence of lines*, never a zero amount: a cart taken to zero by a full-value coupon still has goods going out and still pays for delivery.

4. **`Money.Round` everywhere**, which is half-away-from-zero. C#'s default `Math.Round` is banker's rounding and disagrees with the client at exactly the midpoints a percentage discount produces. See Phase 01.

5. **The percentage is `amount * (percentOff / 100m)`**, not `(amount * percentOff) / 100`. The frontend's comment records that the two forms disagreed by a cent on 10% of $12.75. With `decimal` in C# the two happen to agree, but write the same form so the two implementations can be compared line by line.

`itemSavings` is the per-product `CompareAtPrice - Price` reduction summed over the cart. It is **informational and already inside `subtotal`** — line prices are the reduced prices. Never subtract it.

### Line quantity limits

Every line is 1 to `CommerceOptions.MaxLineQuantity` (10). Reaching the cap is a validation failure (422), not a silent clamp — a client asking for 15 should be told, not quietly given 10 and left showing the wrong number.

The exception is `POST /cart/merge`, where clamping *is* correct. See below.

### Stock validation on add and update

A cart is not a reservation, but it should not let someone build a cart that cannot be bought.

- **`Tracked` product**: requested quantity must not exceed current `StockQuantity`. Exceeding it is `ResultError.Conflict("Product", ...)` → **409**, naming the available quantity.
- **`Unlimited` product**: no check.
- **Inactive or soft-deleted product**: `404`. It is not in the catalogue, so as far as a customer is concerned it does not exist.

`GET /cart` does **not** re-validate stock. It returns the current `stockStatus` on each nested product, which is what the cart page renders, and lets checkout be the thing that refuses. A cart that errors on load because something sold out overnight is worse than a cart that shows "Out of stock" against one row.

> This is a check, not a hold. Two customers can both hold the last unit in their carts. Phase 08 decides who gets it, atomically, at order placement. Reserving stock on add-to-cart would mean abandoned carts starving real buyers.

### Merge on sign-in

`POST /cart/merge` takes the anonymous cart and folds it into the user's.

| Case | Rule |
|---|---|
| Product only in the local cart | Added |
| Product only in the server cart | Kept |
| Product in both | **Quantities summed**, then clamped to `MaxLineQuantity` |
| Product no longer exists, or is inactive | **Silently dropped** |
| Local quantity exceeds stock | **Clamped to available stock**, not rejected |

Summing rather than taking the maximum: the two carts represent two separate intentions to buy, and the customer can decrement afterwards. Taking the maximum would silently discard one of them.

**Clamping rather than failing is the whole point of this endpoint.** It runs during sign-in, and a sign-in that fails because a cart saved three weeks ago now exceeds stock is an awful experience for a problem the customer cannot see. Merge is best-effort by design; every other cart endpoint is strict.

Merge does not carry the coupon code. The local cart stores one, but its eligibility was judged against a different basket. The client re-applies it after merging and gets a fresh answer.

Cap the request at 50 lines. A local cart holds at most a handful; anything larger is not a real cart.

### One cart per user, created on demand

`GET /cart` for a user with no row returns an empty cart **without creating one**. A row appears on the first mutation. Otherwise every anonymous-turned-signed-in visitor who glances at the cart icon leaves an empty row behind.

The unique filtered index on `UserId` makes a concurrent double-create a constraint violation rather than two carts. Catch it, re-read, continue.

### Ownership

Every operation resolves the cart from `ICurrentUser.Id`. **The cart id never appears in a route or a request body**, so there is no id to tamper with and no ownership check to forget. `/cart/items/{productId}` scopes by the caller's cart, so a product id belonging to someone else's cart line simply is not found.

### Change tracking

`AppDbContext` is `NoTrackingWithIdentityResolution` globally. Cart mutations load a `Cart` with its `Items`, change them and save — so the repository's load method for the write path **must call `.AsTracking()`**. A no-tracking cart mutated and saved produces no UPDATE and no error, and the endpoint returns a cart that looks right and did not persist.

Give the repository two methods, named so the difference is obvious:

```csharp
/// <summary>Loads the user's cart for mutation, with items and products tracked.</summary>
Task<Cart?> GetForUpdateAsync(Guid userId, CancellationToken ct = default);

/// <summary>Loads the user's cart for display, untracked, with everything the DTO needs.</summary>
Task<Cart?> GetForDisplayAsync(Guid userId, CancellationToken ct = default);
```

The display method needs the same `Include` chain and `.AsSplitQuery()` as Phase 03's product query, because `CartLineDto` nests a full `ProductSummaryDto`.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Class-level `[Authorize]` on `CartController`. Every action, no exceptions. |
| Authorization | Ownership by construction: the cart is resolved from `ICurrentUser.Id` and its id is never accepted as input. |
| Role checks | None. A cart is a customer concern; admins have no cart endpoints. |
| Validation | A validator for each of the three request DTOs, invoked first in each service method. |
| Sensitive data | A cart reveals purchase intent, so it must never be readable by another user. The design makes that structurally impossible rather than relying on a check. |
| Concurrency | Unique index on `UserId` for double-create; unique index on `(CartId, ProductId)` for double-add. Two tabs adding the same product concurrently is last-write-wins on quantity, which is acceptable — the customer sees the result and can correct it. |
| Rate limiting | Global `fixed` policy only. Cart mutation is cheap and legitimate users are chatty. |

### Two things not to do

- **Do not accept a `cartId`.** The moment a cart id is a route parameter it is a thing to enumerate, and the ownership check becomes something a future endpoint can forget.
- **Do not accept prices or totals in any request.** The only numbers a client sends are quantities. This is the entire reason the phase exists, and it is worth restating in the DTOs' XML docs so nobody helpfully adds a `unitPrice` field "so the client can show it immediately".

---

## Frontend Contract

This phase satisfies `CartService` (`manga-store\src\app\core\catalog\cart.service.ts`).

| Frontend method | Endpoint |
|---|---|
| `restore()` | `GET /cart` |
| `add(product, qty)` | `POST /cart/items` |
| `setQuantity(id, qty)` | `PUT /cart/items/{productId}` (0 removes) |
| `remove(id)` | `DELETE /cart/items/{productId}` |
| `clear()` | `DELETE /cart` |
| `lines` / `count` / `totals` | Derived from the `CartDto` in the last response |
| — | `POST /cart/merge`, new, called after sign-in |

### The synchronous-to-asynchronous change

`CartService`'s mutators are `void` and its state is exposed as signals. Server ownership makes every mutation a round trip. The abstract class's own doc-comment already anticipates this.

The shape that keeps the components unchanged: keep the signals, make the mutators fire-and-update.

```ts
abstract add(product: ProductSummary, quantity?: number): Observable<void>;
```

…with the service updating its `lines` and `totals` signals from the returned `CartDto`. Components that ignore the observable keep working; the cart page can subscribe to show a spinner.

Two behaviours to preserve while doing it:

- **Optimistic update, then reconcile.** The stepper should feel instant. Update the signal, fire the request, and overwrite from the response — including on failure, where the response is the server's cart and the optimistic guess was wrong.
- **`priceCart()` stops being the authority.** It can stay for the anonymous local cart, but once signed in, `totals` comes from `CartDto.totals` and nothing recomputes it. Two calculators disagreeing by a cent is exactly the bug this phase removes.

### The anonymous cart stays

Signed-out visitors keep `InMemoryCartService` and `localStorage`. On successful sign-in, `AuthService` calls `POST /cart/merge` with the local lines, then clears `mangastore.cart`. On sign-out, the server cart is forgotten and the local one starts empty.

### Totals must match to the cent

Until the frontend stops calculating, `CART_RULES` and `CommerceOptions` must agree exactly: `50`, `4.99`, `7.50`, `0.14`, `10`. A discrepancy shows as a cart that changes its total on refresh.

---

## Testing

### Unit tests (`MangaStore.UnitTests`)

Pricing first — it is the part with real arithmetic. Substitute `IOptions<CommerceOptions>` with the documented defaults.

| Test | Asserts |
|---|---|
| `CartPricingTests.EmptyCart_ChargesNothing` | All totals zero, including shipping, and currency falls back to `DefaultCurrency`. |
| `CartPricingTests.EmptyCartWithExpress_StillChargesNoShipping` | The surcharge is not a fee in its own right. |
| `CartPricingTests.BelowThreshold_ChargesFlatRate` | Subtotal 49.99 → shipping 4.99. |
| `CartPricingTests.AtThreshold_ShipsFree` | Subtotal exactly 50 → shipping 0. Inclusive boundary. |
| `CartPricingTests.Express_AddsSurchargeToFreeShipping` | Subtotal 60, express → shipping 7.50, not 12.49. |
| `CartPricingTests.TaxIsChargedOnPostDiscountAmount` | Subtotal 100, 10% coupon → tax on 90, not on 100. |
| `CartPricingTests.FreeShippingIsJudgedBeforeDiscount` | **The regression guard for the documented bug.** Subtotal 50, 5% coupon → shipping stays 0 and the total does not exceed the undiscounted one. |
| `CartPricingTests.ItemSavingsIsNotSubtracted` | A cart with `compareAtPrice` lines: `total` ignores `itemSavings` entirely. |
| `CartPricingTests.RoundingMatchesFrontendAtMidpoint` | 10% of a 12.75 line → 1.28, not 1.27. |
| `CartPricingTests.CurrencyComesFromFirstLine` | |

Then the service:

| Test | Asserts |
|---|---|
| `CartServiceTests.Get_NoCart_ReturnsEmptyWithoutCreatingOne` | Empty `CartDto`, and the repository's add was never called. |
| `CartServiceTests.Add_ExistingProduct_Increments` | 2 then 3 → one line of 5. |
| `CartServiceTests.Add_BeyondMaxQuantity_Returns422` | Not a silent clamp. |
| `CartServiceTests.Add_BeyondStock_Returns409NamingAvailable` | |
| `CartServiceTests.Add_UnlimitedProduct_SkipsStockCheck` | |
| `CartServiceTests.Add_InactiveProduct_Returns404` | Not 409, not 422. |
| `CartServiceTests.Update_ToZero_RemovesLineAndSucceeds` | |
| `CartServiceTests.Update_UnknownProduct_Returns404` | |
| `CartServiceTests.Clear_AlsoClearsCouponCode` | |
| `CartServiceTests.Clear_EmptyCart_Succeeds` | Idempotent. |
| `CartServiceTests.Merge_SumsOverlappingQuantities` | Local 3 + server 4 → 7. |
| `CartServiceTests.Merge_SumBeyondMax_ClampsInsteadOfFailing` | Local 8 + server 6 → 10, success. |
| `CartServiceTests.Merge_BeyondStock_ClampsToAvailable` | |
| `CartServiceTests.Merge_UnknownOrInactiveProduct_IsDropped` | Success, line absent. |
| `CartServiceTests.Merge_DoesNotCarryCouponCode` | |
| `CartServiceTests.Anonymous_IsUnauthorized` | Every method, with no `ICurrentUser.Id`. |

### Integration tests (`MangaStore.IntegrationTests`)

| Test | Asserts |
|---|---|
| `CartApiTests.AllEndpoints_RequireAuthentication` | Six routes, no token, `401` each. |
| `CartApiTests.MutationsReturnFullRecalculatedCart` | Every mutating response carries lines and totals. |
| `CartApiTests.DeleteCart_Returns204WithNoBody` | The only bodyless response. |
| `CartApiTests.CartIsScopedToCaller` | User A adds; user B's `GET /cart` is empty. **The ownership test.** |
| `CartApiTests.NestedProduct_IsPolymorphicOnKind` | A gift-card line carries `"kind":"giftCard"` and a `denomination` object. |
| `CartApiTests.TotalsMatchFrontendFixture` | A fixed basket priced against a value taken from `pricing.spec.ts`, asserted to the cent. **The cross-stack agreement test.** |
| `CartApiTests.MutatedCart_ActuallyPersists` | Add, then `GET` on a fresh request. Catches the no-tracking trap, which no unit test can. |
| `CartApiTests.ConcurrentFirstMutation_CreatesOneCart` | Two parallel `POST /cart/items` for a user with no cart → one cart, correct quantity. |

### Edge cases

- Adding a product that is soft-deleted between the catalogue page and the click: `404`.
- A cart holding a product that is withdrawn afterwards: `GET /cart` still returns the line, with the product's `stockStatus` as-is. Checkout refuses it; the cart does not hide it, because a line that vanishes without explanation is worse than one the customer can see and remove.
- `PUT` with a negative quantity: `422`.
- Merging an empty list: `422`. There is nothing to merge, and the client should not have called.
- Merging when the user has no cart: creates one.
- A cart whose lines are all `Unlimited`: no stock checks anywhere, totals computed normally.
- A cart of gift cards only: **still charged shipping**, because `priceCart` charges shipping on any non-empty cart. That matches the frontend exactly and is therefore correct for now — but a digital-only cart arguably ships free, and this is a business decision nobody has made. Record it in the phase's PR as an open question rather than deciding it here.

---

## Acceptance Criteria

- [ ] `Cart` and `CartItem` entities with factories, configurations, repository and `DbSet`; one migration `AddCart`.
- [ ] Unique filtered indexes on `Cart.UserId` and `CartItem (CartId, ProductId)`; check constraint `Quantity >= 1`.
- [ ] `CartController` with six actions, class-level `[Authorize]`, every action one line.
- [ ] Every mutating action returns the full recalculated `CartDto`; only `DELETE /cart` returns `204`.
- [ ] `ICartPricingCalculator` implements the five documented rules, reads every constant from `CommerceOptions`, and uses `Money.Round` throughout.
- [ ] Tax on the post-discount amount; free shipping judged on the pre-discount subtotal; empty cart charges no shipping or surcharge.
- [ ] `itemSavings` reported and never subtracted.
- [ ] Quantity capped at `MaxLineQuantity` with a 422 — except in merge, which clamps.
- [ ] Stock checked on add and update for `Tracked` products (409), skipped for `Unlimited`, and **not** re-checked on `GET`.
- [ ] Inactive and soft-deleted products are `404`.
- [ ] Merge sums, clamps to max and to stock, drops unknown products, does not carry the coupon code, and caps at 50 lines.
- [ ] `GET /cart` never creates a row; the first mutation does.
- [ ] No endpoint accepts a cart id, a price, or a total.
- [ ] The write path calls `.AsTracking()`; an integration test proves a mutation persists.
- [ ] `coupon` is `null` and `discount` is `0` throughout — Phase 07 fills them.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.
- [ ] The digital-only-cart shipping question is recorded in the PR as open.

---

## Dependencies

```text
Depends on:
  Phase 01 - CommerceOptions, Money.Round, the change-tracking warning.
  Phase 02 - Product.
  Phase 03 - ProductSummaryDto and the include chain, reused by CartLineDto.
  Phase 05 - stock levels are read for validation. Reservation is NOT used here.

Blocks:
  Phase 07 (coupons) - POST /cart/coupons judges eligibility against this cart,
                       and CartDto.coupon/discount are filled by it.
  Phase 08 (orders)  - the cart is what checkout turns into an order, and the
                       pricing calculator is shared.

Can be implemented independently:
  No - requires Phases 01, 02, 03 and 05. Phase 04 (seed) is not required
  but makes manual testing far easier.
```
