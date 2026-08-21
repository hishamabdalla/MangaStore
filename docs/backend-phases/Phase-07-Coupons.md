# Phase 07 — Coupons and Discounts

**Recommended branch:** `phase-07-coupons`

---

## Objective

Make discounts a server decision. The client sends a code and is told either what it is worth or why not; it never evaluates an expiry, a minimum or a usage count itself, because a client that could would also be a client that could be argued with.

Two scopes: a coupon that reduces the whole cart, and a coupon that reduces one product's line.

---

## Current State

### Backend

No `Coupon` entity, no validation, no discount. Phase 06 left `Cart.CouponCode` as a `nvarchar(40)` that is remembered and never acted on, and `CartTotalsDto.discount` hard-coded to `0` with `coupon` `null`. This phase fills both in.

Phase 01 added `ResultError.Validation(entity, reason, message)`, which exists specifically so the rejections below can carry `Coupon.Expired`-shaped titles through a 422.

### Frontend

Coupon logic is entirely client-side, in `manga-store\src\app\core\catalog\in-memory\coupon.seed.ts`:

```ts
export const COUPON_SEED: readonly CouponSeed[] = [
  { code: 'MANGA10', percentOff: 10, scope: 'cart' },
  { code: 'SHELF20', percentOff: 20, scope: 'cart' },
  /* Tied to one denomination, so the other Steam cards stay full price. */
  { code: 'STEAM10', percentOff: 10, scope: 'item', productId: 'steam-gift-card-70' },
];
```

Three codes, all percentage-off, two cart-wide and one item-scoped. `InMemoryCouponService` raises only `Coupon.NotFound` and `Coupon.NotApplicable`; the other five error codes already exist in `COUPON_ERROR_CODES`, in `ErrorMessageService`, and as translated sentences in `public/i18n/{en,ar}.json`, waiting for a real API.

The promo form lives in the **cart** page's order summary (`features/cart/coupon-form.ts`), not on checkout — the checkout page's error handler says so explicitly.

**Only percentage discounts are modelled anywhere in the frontend.** There is no fixed-amount coupon type in any interface, seed or component.

---

## Scope

| Component | Files |
|---|---|
| Domain | `Coupon`, `CouponScope`, `CouponRedemption`, `ICouponRepository`, `ICouponRedemptionRepository` |
| Application | `Features/Coupons/` — `AppliedCouponDto`, `ApplyCouponRequest` + validator, `CouponProfile`, `ICouponService` / `CouponService`; `CouponErrors` |
| Infrastructure | Configurations, repositories, migration, seeder extension |
| API | Two actions added to `CartController` |

### Out of scope

- **Fixed-amount discounts.** Nothing in the frontend models them, and inventing a discount type no UI can render is exactly what the brief forbids. The schema below can gain an `Amount` column later without disturbing anything.
- **Stacking.** One coupon per cart. No UI shows two.
- **Order-time revalidation.** Phase 08 re-validates and re-prices the code when the order is placed, and this phase provides the method it calls.
- **Admin coupon CRUD.** Phase 13.

---

## Database Changes

### `Coupon`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `Code` | `nvarchar(40)` | **Unique filtered on `IsDeleted = 0`. Stored uppercase** |
| `PercentOff` | `int` | 1–100. Whole percent |
| `Scope` | `int` | `CouponScope { Cart, Item }` |
| `ProductId` | `uniqueidentifier` NULL | FK, restrict delete. Required when `Scope = Item`, null otherwise |
| `StartsAt` | `datetime2` NULL | Not yet valid before this |
| `ExpiresAt` | `datetime2` NULL | Not valid at or after this |
| `MinimumSubtotal` | `decimal(18,2)` NULL | Pre-discount subtotal the cart must reach |
| `UsageLimit` | `int` NULL | Total redemptions across all customers. Null is unlimited |
| `TimesUsed` | `int` | Incremented when an order is placed, not when the code is applied |
| `IsActive` | `bit` | Switch off without deleting |
| `RowVersion` | `rowversion` | For `TimesUsed` under concurrency, and for admin edits |

**Check constraint, not a convention:**

```sql
CHECK ((Scope = 0 AND ProductId IS NULL) OR (Scope = 1 AND ProductId IS NOT NULL))
```

The guideline is emphatic about this and it is right: a scope-and-target mismatch prices a cart wrongly rather than failing loudly. An `item` coupon with no product silently discounts nothing and reaches the customer as "this coupon does not apply to your cart" — a data error wearing the costume of a business rule. Let the database refuse it.

Also `CHECK (PercentOff BETWEEN 1 AND 100)` and `CHECK (ExpiresAt IS NULL OR StartsAt IS NULL OR ExpiresAt > StartsAt)`.

### `CouponRedemption`

`Coupon.AlreadyUsed` means "this customer has used it", which needs a per-customer record. `TimesUsed` alone cannot answer it.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `CouponId` | `uniqueidentifier` | FK, restrict delete |
| `UserId` | `uniqueidentifier` | Bare, as elsewhere |
| `OrderId` | `uniqueidentifier` NULL | FK added in Phase 08 |

**Unique filtered on `(CouponId, UserId)`** — one redemption per customer per code. That is the rule the `AlreadyUsed` error describes, and enforcing it in the index means a race between two simultaneous checkouts cannot produce two.

> Rows are written by **Phase 08**, at order placement. Applying a code to a cart redeems nothing — a customer can apply and remove a coupon all afternoon.

### Migration

```bash
dotnet ef migrations add AddCoupons \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

Two tables. Verify all four check constraints and both filtered unique indexes survived generation — EF sometimes needs `HasCheckConstraint` spelled out rather than inferred.

### Seed extension

Extend Phase 04's `CatalogueSeeder` — do not add a second seeder. The guideline asks for coupons "including at least one expired and one below-minimum, so the rejection paths can be seen working rather than assumed":

| Code | Percent | Scope | Extra |
|---|---|---|---|
| `MANGA10` | 10 | Cart | — |
| `SHELF20` | 20 | Cart | — |
| `STEAM10` | 10 | Item | `steam-gift-card-70` |
| `LASTYEAR` | 15 | Cart | `ExpiresAt` 30 days in the past |
| `BIGSPEND` | 25 | Cart | `MinimumSubtotal` 500 |
| `SOLDOUT` | 30 | Cart | `UsageLimit` 1, `TimesUsed` 1 |

Dates relative to `IDateTime.UtcNow`, for the same reason Phase 04 anchors release dates: a hard-coded expiry stops testing what it was written to test.

`STEAM10` needs the deterministic id from Phase 04's `SeedId("steam-gift-card-70")`. That is exactly why those ids are derived rather than generated.

---

## API Contract

Two actions on the existing `CartController`, inheriting its class-level `[Authorize]`.

### `POST /cart/coupons`

| | |
|---|---|
| Auth | `[Authorize]` |
| Request | `ApplyCouponRequest { string Code }` |
| Success | `200` `CartDto` |
| Errors | `401`, `422` with a `Coupon.*` title |

Rate-limited per user. See Security.

> **Two deliberate deviations from the guideline, both worth the ink.**
>
> **It returns `CartDto`, not `AppliedCoupon`.** Applying a coupon mutates the cart, and Phase 06's rule is that every cart mutation returns the whole recalculated cart. Returning the coupon alone would leave the client to either recompute the discount — the thing this phase exists to prevent — or make a second `GET /cart`. The `AppliedCoupon` the client wants is `CartDto.totals.coupon`, so nothing is lost.
>
> **The request is `{ code }` only.** The guideline's `{ code, subtotal, productIds }` exists for a client-side cart. The cart is server-owned now, so the endpoint already has the basket, and taking a subtotal from the client would mean either trusting it or re-deriving it anyway. Any `subtotal` or `productIds` the client sends is **ignored** — System.Text.Json drops unknown members by default, so no change is needed to accept the frontend's current payload while it catches up.

### `DELETE /cart/coupons`

| | |
|---|---|
| Success | `200` `CartDto` |
| Errors | `401` |

Removing a coupon that is not applied succeeds and returns the cart unchanged. The client's control is a Remove button next to an applied code; making it fail when the code was already gone would only produce an error the customer cannot act on.

### `AppliedCouponDto` — inside `CartTotalsDto.coupon`

```jsonc
{ "code": "MANGA10", "percentOff": 10, "scope": "cart" }
{ "code": "STEAM10", "percentOff": 10, "scope": "item", "productId": "0198c4…" }
```

`productId` is present if and only if `scope` is `item`. The client types this as a discriminated union on `scope` — an item coupon with no product, or a cart coupon carrying one, are both states its shape refuses to describe — so emit `productId` as `null`-omitted for cart scope rather than `null`.

**Nothing else is published.** `StartsAt`, `ExpiresAt`, `MinimumSubtotal`, `UsageLimit`, `TimesUsed` and `IsActive` exist to answer "may this cart use it?" and never reach the client. The eligibility rules are the reason a code was refused, not something a shopper is owed.

### The rejection vocabulary

All 422. `ApiControllerBase` maps `ResultErrorCodes.Validation` to 422; the entity-qualified title comes from Phase 01's three-argument `ResultError.Validation`.

| Title | Meaning | Client sentence key |
|---|---|---|
| `Coupon.NotFound` | No such code, inactive, or outside its window | `errors.coupon.invalid` |
| `Coupon.Expired` | Outside its window — **see the oracle note** | `errors.coupon.expired` |
| `Coupon.AlreadyUsed` | This customer has redeemed it | `errors.coupon.alreadyUsed` |
| `Coupon.NotApplicable` | Nothing in the cart qualifies | `errors.coupon.notApplicable` |
| `Coupon.MinimumNotReached` | Cart is below the minimum | `errors.coupon.minimumNotReached` |
| `Coupon.UsageLimitReached` | Global limit spent | `errors.coupon.usageLimitReached` |

`Coupon.AlreadyApplied` is in `COUPON_ERROR_CODES` but is **client-side only** — `InMemoryCartService` refuses to re-apply the active code locally "rather than at the server, which would happily say yes twice". Keep that behaviour on the client; do not add a server rejection for it. Re-applying the same code server-side is idempotent and succeeds.

---

## Business Rules

### Validation order

Cheapest and least informative first, so an attacker learns as little as possible from a rejection:

1. Normalise: trim, uppercase.
2. Look up by `Code`. Not found, `IsActive = false`, or soft-deleted → `Coupon.NotFound`.
3. Outside `[StartsAt, ExpiresAt)` → see the oracle note below.
4. `UsageLimit` reached (`TimesUsed >= UsageLimit`) → `Coupon.UsageLimitReached`.
5. A `CouponRedemption` exists for this coupon and this user → `Coupon.AlreadyUsed`.
6. Scope is `Item` and the product is not in the cart, or is in it with quantity zero → `Coupon.NotApplicable`.
7. `MinimumSubtotal` set and the cart's **pre-discount** subtotal is below it → `Coupon.MinimumNotReached`.
8. Otherwise: store the code on the cart and return the recalculated `CartDto`.

Step 6 before step 7 is deliberate: "this coupon isn't for anything in your basket" is more actionable than "spend more", and telling someone to spend more on a coupon that would not apply anyway is worse than useless.

### The oracle problem

The guideline is direct about it:

> Do not distinguish "expired" from "never existed" if codes are guessable. A different message for each turns the endpoint into an oracle for probing which campaigns exist.

Make it a configuration switch rather than a hard-coded choice, because the right answer depends on how codes are issued:

```json
"Coupons": { "RevealExpiry": false }
```

- `false` (**default**): an expired or not-yet-started code returns `Coupon.NotFound`, indistinguishable from a code that never existed.
- `true`: returns `Coupon.Expired`. Appropriate only when codes are long, random, and issued to named customers — a customer holding a code they were personally given deserves to know it lapsed rather than being told it was never real.

The seeded `LASTYEAR` exercises whichever branch is configured, and the tests cover both.

The same reasoning does **not** apply to `MinimumNotReached` or `NotApplicable`: by the time either is reached the caller already knows the code is real, so withholding the reason costs a legitimate customer clarity and costs an attacker nothing.

### Cart scope

The percentage comes off the whole pre-discount subtotal.

```text
discount = round(subtotal * (percentOff / 100))
```

Every line contributes, including lines already reduced by their own `CompareAtPrice` — those reductions are already inside `subtotal`.

### Item scope — the whole line, quantity included

```text
lineTotal = round(unitPrice * quantity)
discount  = round(lineTotal * (percentOff / 100))
```

**An item coupon reduces the entire line, not one unit of it.** Three `$70` Steam cards with `STEAM10` is `10%` off `3 × 71.49 = 214.47`, giving `21.45` — not `7.15`. It is a discount on the product, not on one copy of it. This is spelled out in the brief, in the guideline, and in `pricing.model.ts`, because it is the single most likely thing to be implemented wrongly.

Summed rather than found, so it stays right if a product ever splits across lines:

```csharp
decimal DiscountBase(IReadOnlyList<PricedLine> lines, decimal subtotal, AppliedCoupon coupon) =>
    coupon.Scope == CouponScope.Cart
        ? subtotal
        : Money.Round(lines.Where(l => l.ProductId == coupon.ProductId)
                           .Sum(l => Money.Round(l.UnitPrice * l.Quantity)));
```

### Refuse, do not return a coupon worth zero

An item coupon whose product is not in the cart is `Coupon.NotApplicable`, **not** a valid coupon with a discount of zero. "Valid, but zero" leaves the client to explain a discount that never arrives, and that explanation is the server's to give.

### A coupon can become inapplicable after it is applied

The customer applies `STEAM10`, then removes the Steam card. The stored `CouponCode` now discounts nothing.

`GET /cart` re-evaluates the stored code on every read and reports honestly:

- Still eligible → `totals.coupon` populated, `discount` computed.
- No longer eligible → `totals.coupon` is `null` and `discount` is `0`, but **the code stays on the cart**.

Keeping it matters. `InMemoryCartService`'s restore logic already draws this distinction: a `Coupon.NotApplicable` rejection is kept because it is a fact about the *cart* — the product was removed — not about the coupon, and it revives when the product comes back. A code that vanished when the customer removed an item and did not return when they re-added it would look broken.

A code that has become genuinely dead — expired, limit reached, deactivated — is **dropped** from the cart on the next read, since nothing the customer does will revive it.

### `TimesUsed` and redemption belong to order placement

Applying a code reserves nothing. `TimesUsed` increments and a `CouponRedemption` row is written **only** when Phase 08 places an order, inside the same transaction as the order and the stock decrement.

Otherwise a single-use campaign is exhausted by people typing codes into carts they abandon.

Phase 08 calls one method from this phase:

```csharp
/// <summary>Re-validates a code against a cart and returns what it is worth, or why not.</summary>
Task<Result<AppliedCoupon>> ValidateForCartAsync(
    string code, Guid userId, IReadOnlyList<PricedLine> lines, CancellationToken ct = default);
```

Phase 08 owns the incrementing, because it owns the transaction.

### Codes are stored and compared uppercase

`normalizeCouponCode` on the client is `code.trim().toUpperCase()`. The server does the same before lookup and before storing, so `manga10`, `MANGA10 ` and ` Manga10` are one coupon.

Do not rely on a case-insensitive collation for this. It usually holds on SQL Server and does not hold on SQLite, which is what the integration tests run — a test suite that passes because of a collation default is a test suite that will fail on a different one.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Both actions require `[Authorize]`, inherited from `CartController`. `Coupon.AlreadyUsed` cannot be evaluated for an anonymous caller, and a coupon endpoint open to the internet is a free brute-force target. |
| Authorization | Ownership by construction — the cart comes from `ICurrentUser.Id`. |
| Role checks | None. Phase 13 restricts coupon *management* to `Roles.Admin`. |
| Validation | `ApplyCouponRequestValidator`: not empty, at most 40 characters, `^[A-Za-z0-9-]+$`. Rejecting exotic characters up front keeps the `LIKE`-free lookup simple and the logs readable. |
| Sensitive data | Only `code`, `percentOff`, `scope` and `productId` are ever published. Every eligibility field stays server-side. |
| Concurrency | `RowVersion` on `Coupon` for `TimesUsed`; the unique index on `(CouponId, UserId)` makes a double redemption a constraint violation rather than a second discount. |
| Rate limiting | **Required.** See below. |

### Brute force is the real threat here

A coupon endpoint that answers unlimited guesses is a discovery tool for live campaign codes. Short, memorable, human-issued codes — `MANGA10`, `SHELF20` — are guessable by design.

Add a policy in `Program.cs` alongside the `auth` policy Phase 01 restored, partitioned by **user id** rather than IP, since the endpoint is authenticated:

```csharp
options.AddPolicy(RateLimitOptions.CouponPolicy, httpContext =>
    RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.FindFirstValue(AppClaimTypes.Subject) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.CouponPermitLimit,   // default 20
            Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
        }));
```

`[EnableRateLimiting(RateLimitOptions.CouponPolicy)]` on the `POST` action only — removing a coupon needs no limit.

Twenty attempts a minute is generous for someone typing a code off an email and useless for enumeration. Log failed attempts at Warning with the user id and the attempted code: a customer mistyping once looks nothing like an account trying two thousand codes, and only the log will show the difference.

### Timing

The validation order short-circuits, so a nonexistent code returns faster than an eligible one. That is a timing side channel. It is not worth defending here — the difference is one indexed lookup against a table with tens of rows, well inside network jitter — but do not add anything that widens it, such as an external call or a hash comparison in the not-found path.

---

## Frontend Contract

| Frontend method | Endpoint |
|---|---|
| `CartService.applyCoupon(code)` | `POST /cart/coupons` |
| `CartService.removeCoupon()` | `DELETE /cart/coupons` |
| `CouponService.validate(code, ctx)` | `POST /cart/coupons` — the `ctx` argument becomes unused |

`applyCoupon` returns `Observable<AppliedCoupon>`; the response is a `CartDto`, so the client maps `dto.totals.coupon` out of it and updates its `totals` signal from the same response. One round trip instead of two.

`CouponContext` (`{ subtotal, currency, productIds }`) can be deleted once the swap lands. The server derives all three from the cart it owns.

### What the order summary already renders

`cart.page.html` shows a discount row **only when `totals.discount > 0`**, labelled `cart.discountItem` or `cart.discountPromo` depending on `coupon.scope`, with the code beside it and the amount as `−$X.XX` in success green. An item coupon additionally shows a `−$X.XX` chip on **its own line only** via `couponDiscountOn(line, coupon)`.

Both need `scope` and, for item scope, `productId` — which is why the union is emitted precisely rather than as an optional field.

There is **no "discounted subtotal" row**. Subtotal is shown pre-discount and the discount is its own line. The DTO already matches this; do not add a computed field for a row that does not exist.

### Error rendering

`ErrorMessageService` maps `ProblemDetails.title` to a translated sentence. All six titles above are already mapped in `public/i18n/{en,ar}.json`. Raw backend messages are never shown, so the `detail` string is for logs and support, not for the customer — but keep it accurate anyway.

---

## Testing

### Unit tests (`MangaStore.UnitTests`)

| Test | Asserts |
|---|---|
| `CouponServiceTests.UnknownCode_ReturnsNotFound` | 422, title `Coupon.NotFound`. |
| `CouponServiceTests.InactiveCoupon_ReturnsNotFound` | Not a separate error. |
| `CouponServiceTests.Expired_WithRevealExpiryFalse_ReturnsNotFound` | The oracle default. |
| `CouponServiceTests.Expired_WithRevealExpiryTrue_ReturnsExpired` | The other branch. |
| `CouponServiceTests.NotYetStarted_IsTreatedAsExpired` | `StartsAt` in the future takes the same path. |
| `CouponServiceTests.UsageLimitReached_ReturnsUsageLimitReached` | |
| `CouponServiceTests.AlreadyRedeemedByThisUser_ReturnsAlreadyUsed` | |
| `CouponServiceTests.AlreadyRedeemedByAnotherUser_IsStillUsable` | The redemption is per customer. |
| `CouponServiceTests.ItemScope_ProductNotInCart_ReturnsNotApplicable` | **Not** a coupon worth zero. |
| `CouponServiceTests.BelowMinimum_ReturnsMinimumNotReached` | |
| `CouponServiceTests.NotApplicableIsCheckedBeforeMinimum` | The ordering. |
| `CouponServiceTests.MinimumIsJudgedOnPreDiscountSubtotal` | |
| `CouponServiceTests.CodeIsNormalisedBeforeLookup` | ` manga10 ` finds `MANGA10`. |
| `CouponServiceTests.Apply_DoesNotIncrementTimesUsed` | Redemption is Phase 08's. |
| `CouponServiceTests.Apply_WritesNoRedemptionRow` | |
| `CouponServiceTests.ReApplyingSameCode_Succeeds` | No server-side `AlreadyApplied`. |
| `CouponServiceTests.Remove_WhenNoCouponApplied_Succeeds` | |
| `CouponServiceTests.PublishedDtoOmitsEligibilityFields` | Serialise `AppliedCouponDto` and assert the JSON has exactly three or four members. **The leak test.** |
| `CouponServiceTests.CartScopeDto_OmitsProductId` | Not `null` — absent. |

Pricing, extending Phase 06's calculator tests:

| Test | Asserts |
|---|---|
| `CartPricingTests.ItemCoupon_DiscountsWholeLineIncludingQuantity` | 3 × 71.49 at 10% → 21.45, **not** 7.15. The rule most likely to be got wrong. |
| `CartPricingTests.ItemCoupon_LeavesOtherLinesUntouched` | A `$20` Steam card beside the `$70` one keeps its full price. |
| `CartPricingTests.CartCoupon_AppliesToWholeSubtotal` | |
| `CartPricingTests.ItemCoupon_ProductAbsent_DiscountsNothing` | The calculator's own defence, independent of validation. |
| `CartPricingTests.ItemCoupon_SplitAcrossTwoLines_SumsBoth` | |
| `CartPricingTests.CouponTakingCartToZero_StillChargesShipping` | Emptiness is the absence of lines. |

### Integration tests (`MangaStore.IntegrationTests`)

| Test | Asserts |
|---|---|
| `CouponApiTests.Apply_ReturnsFullCartWithDiscount` | `totals.discount` and `totals.coupon` both populated. |
| `CouponApiTests.Apply_ItemScope_EmitsProductId` | And cart scope does not. |
| `CouponApiTests.Apply_Rejection_Is422WithCouponTitle` | Raw JSON: `"status":422` and `"title":"Coupon.NotFound"`. **Pins the Phase 01 `ResultError` overload end to end.** |
| `CouponApiTests.Anonymous_Is401` | Both actions. |
| `CouponApiTests.RemovedProduct_KeepsCodeButZeroesDiscount` | Apply `STEAM10`, remove the card, `GET /cart` → `coupon` null, `discount` 0, code still stored. Re-add → discount returns. |
| `CouponApiTests.DeadCode_IsDroppedFromCart` | An expired code is removed on the next read. |
| `CouponApiTests.Apply_BeyondRateLimit_Returns429` | With a low `CouponPermitLimit` for the class. |
| `CouponApiTests.LowercaseCode_IsAccepted` | Proves normalisation rather than collation. Runs on SQLite, where the collation would not save it. |
| `CouponApiTests.SeededRejectionCoupons_EachProduceTheirError` | `LASTYEAR`, `BIGSPEND`, `SOLDOUT` each hit their intended branch. The seed earning its keep. |

### Authorization tests

Both actions with no token, with an expired token, and with a valid `Customer` token. The first two are 401; the third succeeds. A coupon applied by user A must never appear on user B's cart — covered by Phase 06's ownership test, worth repeating here with a coupon attached.

### Edge cases

- Code of exactly 40 characters: accepted. 41: 422.
- Code containing a space in the middle: 422 from the character-set rule.
- `PercentOff = 100`: the cart's goods cost zero. Shipping and tax still compute — tax on zero is zero, shipping is unchanged because the threshold is judged pre-discount.
- An item coupon on a product with a `CompareAtPrice`: the percentage comes off the **current** price, not the compare-at price. The card shows both reductions, and they are not the same discount.
- A coupon whose target product is soft-deleted: `Coupon.NotApplicable`, since it cannot be in any cart.
- A coupon with `StartsAt` and `ExpiresAt` both null: always in window.
- Two tabs applying different codes simultaneously: last write wins on `Cart.CouponCode`. Acceptable — the customer sees the result.

---

## Acceptance Criteria

- [ ] `Coupon` and `CouponRedemption` entities, configurations, repositories and `DbSet`s; migration `AddCoupons`.
- [ ] Check constraint enforcing scope-and-target agreement; `PercentOff` between 1 and 100; `ExpiresAt` after `StartsAt`.
- [ ] Unique filtered indexes on `Coupon.Code` and `CouponRedemption (CouponId, UserId)`.
- [ ] `POST /cart/coupons` and `DELETE /cart/coupons` on `CartController`, both returning `CartDto`.
- [ ] `POST` carries `[EnableRateLimiting(RateLimitOptions.CouponPolicy)]`, partitioned by user id.
- [ ] All six rejections return 422 with the exact `Coupon.*` titles listed.
- [ ] `Coupons:RevealExpiry` configuration exists, defaults to `false`, and both branches are tested.
- [ ] Validation runs in the documented order; `NotApplicable` is checked before `MinimumNotReached`.
- [ ] **An item coupon discounts the whole line including quantity**, with a test proving 3 × 71.49 at 10% is 21.45.
- [ ] An item coupon whose product is absent is refused, never returned worth zero.
- [ ] `AppliedCouponDto` publishes only `code`, `percentOff`, `scope` and — for item scope only — `productId`. A serialisation test proves nothing else leaks.
- [ ] Applying a coupon does not increment `TimesUsed` and writes no `CouponRedemption`.
- [ ] `ValidateForCartAsync` exists for Phase 08.
- [ ] A stored code that becomes inapplicable is kept and reported as zero; a dead code is dropped.
- [ ] Codes normalised to uppercase in application code, not by collation.
- [ ] Seeder extended with six coupons including an expired, a below-minimum and a used-up one, all with relative dates.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 01 - ResultError.Validation(entity, reason, message), Money.Round,
             RateLimitOptions.
  Phase 02 - Product, for the item-scope foreign key.
  Phase 04 - the seeder this phase extends, and the deterministic id for
             steam-gift-card-70.
  Phase 06 - the cart the coupon is judged against, and the pricing calculator
             this phase completes.

Blocks:
  Phase 08 (orders)     - re-validates through ValidateForCartAsync, increments
                          TimesUsed and writes CouponRedemption.
  Phase 12 (dashboard)  - coupon usage statistics, if surfaced.
  Phase 13 (admin CRUD) - coupon management.

Can be implemented independently:
  No - requires Phase 06 in particular. The guideline suggests doing coupons
  before or with orders, and that holds: an order that re-validates a code
  needs somewhere to ask.
```
