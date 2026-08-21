# Phase 13 — Admin CRUD

**Recommended branch:** `phase-13-admin-crud`

---

## Objective

Let an administrator run the shop: create and edit products, manage categories and coupons, adjust stock, and move orders through their lifecycle.

And, just as importantly, let nobody else do any of it. Every endpoint here is a way to change prices, stock and availability, which makes this the phase where the authorization tests matter more than the feature tests.

---

## Current State

### Backend

Every read path exists. No write path does, except the customer-facing cart, coupon-apply and order-placement endpoints, all of which are scoped to the caller's own data.

`IInventoryService.AdjustAsync` (Phase 05) and `IPaymentConfirmationService.ConfirmAsync` (Phase 11) are written and have no HTTP surface. This phase gives them one.

The only role-guarded route so far is Phase 12's dashboard, plus Phase 09's gift-card code controller.

### Frontend

**No admin UI exists.** `roleGuard` is defined and used by zero routes; there is no `/admin` route, no product form, no category form, no order management screen. `ROLES.admin = 'Admin'` and `AuthService.isAdmin` are the whole of it.

So, as with Phase 12, this phase defines a contract with no consumer. The shape is ours to choose well.

---

## Scope

| Component | Files |
|---|---|
| Application | `Features/Admin/Catalogue/` — create/update requests and validators for products and categories, `AdminProductDto`, `AdminProductQueryParams`, `IAdminCatalogueService`; `Features/Admin/Coupons/` — coupon requests, `AdminCouponDto`, `IAdminCouponService`; `Features/Admin/Orders/` — `UpdateOrderStatusRequest`, `IAdminOrderService`; profiles for each |
| Infrastructure | Write-side repository methods, slug uniqueness helpers |
| API | Write actions added to `CatalogController`; new `AdminProductsController`, `AdminCouponsController`; a status action on `OrdersController` |

### Out of scope

- **Cover uploads.** Phase 14.
- **Gift-card code import.** Already shipped in Phase 09, on its own controller.
- **User management.** No requirement, no UI, and a user-administration surface is a liability that should be justified before it is built. `IIdentityService` gained counting methods in Phase 12; it gains nothing here.
- **Bulk operations.** No importer, no batch price change. One product at a time until something asks otherwise.

---

## Database Changes

**None.** Every column these endpoints write already exists. `Product.RowVersion` (Phase 02), `Coupon.RowVersion` (Phase 07) and `Order.RowVersion` (Phase 08) were all added in anticipation of exactly this, and this is where they are finally read.

---

## API Contract

### Where the write actions live

Product and category writes go on **`CatalogController`**, beside the public reads, because the guideline specifies `/catalog/products` and `/catalog/categories` and those are resource paths, not audience paths.

That makes `CatalogController` a controller with both anonymous and admin actions, which is the exact shape `CLAUDE.md` warns about:

> Don't put `[AllowAnonymous]` on a controller that also has `[Authorize]` actions — the class-level attribute wins and silently opens them. Apply it per action.

So: **no class-level attribute of either kind on `CatalogController`.** Every read carries its own `[AllowAnonymous]`; every write carries its own `[Authorize(Roles = Roles.Admin)]`. Phase 03 already established the first half.

The admin-only *listing* endpoint goes on a separate controller, because it is a different resource — the catalogue including things customers cannot see.

### Products

| Method | Route | Auth | Request | Success | Errors |
|---|---|---|---|---|---|
| `POST` | `/catalog/products` | Admin | `CreateProductRequest` | `201` `AdminProductDto` + `Location` | 401, 403, 409, 422 |
| `PUT` | `/catalog/products/{id}` | Admin | `UpdateProductRequest` | `200` `AdminProductDto` | 401, 403, 404, 409, 422 |
| `DELETE` | `/catalog/products/{id}` | Admin | — | `204` | 401, 403, 404, 409 |
| `PUT` | `/catalog/products/{id}/inventory` | Admin | `StockAdjustmentRequest` | `200` `StockLevelDto` | 401, 403, 404, 409, 422 |
| `GET` | `/admin/products` | Admin | `AdminProductQueryParams` | `200` `PaginatedList<AdminProductDto>` | 401, 403, 422 |

`GET /admin/products` exists because the public listing filters `IsActive = true`. Without it an administrator who deactivates a product can never find it again to reactivate it. It accepts the same filters as the public query plus `status` (`all` by default, or `active`, `inactive`) and `includeDeleted`.

`CreateProductRequest` is polymorphic on `kind`, mirroring the read DTOs:

```jsonc
// Shared
{
  "kind": "manga",
  "slug": "ashfall-ronin",            // optional; derived from the English title if omitted
  "price": 12.99,
  "compareAtPrice": null,
  "currency": "USD",
  "inventoryMode": "tracked",
  "stockQuantity": 40,
  "isActive": true,
  "releasedOn": "2026-05-14",
  "categoryIds": ["…"],
  "translations": [
    { "languageCode": "en", "title": "Ashfall Ronin", "description": "…" },
    { "languageCode": "ar", "title": "رونين الرماد", "description": "…" }
  ],

  // kind = manga
  "isbn": "978-1-234-56789-0",
  "readingDirection": "rightToLeft",
  "ageRating": "16+",
  "authorId": "…",
  "publisherId": "…",
  "volumes": [{ "number": 1, "pageCount": 192, "releasedOn": "2025-11-02",
                "translations": [{ "languageCode": "en", "title": "…" }] }]

  // kind = giftCard
  // "brandId", "denominationAmount", "denominationCurrency", "deliveryType",
  // "regionTranslations", "termsTranslations", "redemptionSteps"
}
```

`UpdateProductRequest` is the same shape plus a required `rowVersion`, and **without** `kind` — a manga cannot become a gift card. Changing kind means deleting and re-creating, because the detail row and everything hanging off it differ.

`AdminProductDto` is the public detail DTO plus `isActive`, `inventoryMode`, `stockQuantity`, `rowVersion`, `createdAt`, `updatedAt`, and **all** translations rather than one resolved string. An admin edits every language; a customer reads one.

### Categories

| Method | Route | Request | Success | Errors |
|---|---|---|---|---|
| `POST` | `/catalog/categories` | `CreateCategoryRequest` | `201` `AdminCategoryDto` | 401, 403, 409, 422 |
| `PUT` | `/catalog/categories/{id}` | `UpdateCategoryRequest` | `200` `AdminCategoryDto` | 401, 403, 404, 409, 422 |
| `DELETE` | `/catalog/categories/{id}` | — | `204` | 401, 403, 404, **409** |

`{ slug, kind, translations: [{ languageCode, name }] }`.

### Coupons

`AdminCouponsController` at `/api/v1/admin/coupons`, class-level `[Authorize(Roles = Roles.Admin)]` — no anonymous action, so the class-level attribute is safe.

| Method | Route | Success |
|---|---|---|
| `GET` | `/admin/coupons` | `200` `PaginatedList<AdminCouponDto>` |
| `GET` | `/admin/coupons/{id}` | `200` `AdminCouponDto` |
| `POST` | `/admin/coupons` | `201` `AdminCouponDto` |
| `PUT` | `/admin/coupons/{id}` | `200` `AdminCouponDto` |
| `DELETE` | `/admin/coupons/{id}` | `204` |

`AdminCouponDto` carries **everything** — `startsAt`, `expiresAt`, `minimumSubtotal`, `usageLimit`, `timesUsed`, `isActive`, `rowVersion`. Phase 07's rule that eligibility fields are never published applies to the **customer** endpoint. An admin managing a campaign has to see them.

The customer path stays `/cart/coupons` and is unchanged.

### Orders

| Method | Route | Auth | Request | Success | Errors |
|---|---|---|---|---|---|
| `PUT` | `/orders/{id}/status` | Admin | `UpdateOrderStatusRequest { OrderStatus Status, string? RowVersion }` | `200` `OrderDto` | 401, 403, 404, 409, 422 |
| `GET` | `/admin/orders` | Admin | filters + pagination | `200` `PaginatedList<AdminOrderDto>` | 401, 403, 422 |

`PUT /orders/{id}/status` goes on `OrdersController`, which has class-level `[Authorize]`. The action adds `[Authorize(Roles = Roles.Admin)]`, narrowing it — that composes correctly, because a second `[Authorize]` on the action is an additional requirement, not a replacement.

`GET /admin/orders` is separate from the customer's `GET /orders`, which is scoped to the caller. Merging them behind a role check would mean one endpoint whose result set depends on who is asking — the exact shape that produces a leak when someone later adds a `userId` filter.

---

## Business Rules

### Slugs

If `slug` is omitted, derive it from the English title: lowercase, non-alphanumerics to hyphens, collapse repeats, trim, truncate to 160.

Arabic-only titles produce an empty slug. Reject with 422 asking for an explicit slug rather than emitting `product-a1b2c3` — a machine-generated slug in a URL is worse than making the administrator choose one.

Collision is `409` `Product.Conflict`, never an auto-suffix. `ashfall-ronin-2` created silently is a slug nobody meant.

Phase 01's filtered unique index means a **soft-deleted** product does not hold its slug. That is the point of the filter, and it means re-creating a deleted product with its original slug works.

**Slugs are editable but should not be.** They are public identifiers in shared links, and changing one breaks every link to it. Allow it — an administrator may need to fix a typo before anyone has seen it — and log at Warning when it happens.

### Optimistic concurrency

`PUT` on a product, coupon or order requires `rowVersion` from the last read. A mismatch is `409` `Product.Conflict`: "This product was changed by someone else. Reload and try again."

Two administrators editing one product is exactly the case `RowVersion` was added for in Phase 02, and last-write-wins on a price is how a discount gets silently reverted.

Catch `DbUpdateConcurrencyException` in Infrastructure and translate it to a `ResultError.Conflict` — the service returns `Result` and never throws, per `CLAUDE.md`.

> **Stock is different.** `PUT /catalog/products/{id}/inventory` goes through Phase 05's `AdjustAsync`, which uses `RowVersion` for admin-versus-admin conflict, while order placement uses a guarded `ExecuteUpdate` and no `RowVersion` at all. Two mechanisms, two jobs, as Phase 05 says. Do not unify them.

### Deletion is soft, and the ledger is not

`DELETE` stages the entity and `SoftDeleteInterceptor` converts it to `IsDeleted = true`. The product vanishes from every query filtered view.

**A product with orders against it is still deletable.** `OrderLine` snapshots `Title` and `UnitPrice` and holds no foreign key to `Product`, precisely so history survives. `StockMovement` and `GiftCardCode` both use restrict delete, but soft delete is an UPDATE, so neither blocks.

Refuse deletion in one case: a gift-card product with `Available` codes in its pool. Deleting it would strand sellable inventory in a table nothing lists. `409`, asking the administrator to void the codes first.

### Category deletion refuses while products reference it

`409` `Category.Conflict` when any non-deleted product is in the category, naming the count.

The alternative — cascade the removal from `ProductCategory` — silently drops products out of the navigation menu, and the administrator finds out from a customer. Refusing forces the reassignment to be deliberate.

An empty category deletes cleanly.

### Translations must be complete

Create requires a translation for **every** language in `SupportedLanguages.All` (`en`, `ar`). Update may send a subset; missing languages are left as they are, and there is no way to delete a translation.

Phase 03 falls back to English when a translation is missing, so an incomplete product would render half-English to an Arabic customer. That is worth preventing at the point of entry rather than papering over at read time.

`title` is required in each; `description` is optional.

### Validation that mirrors the schema

Every check constraint from Phase 02 gets a validator, so an administrator sees a 422 naming the field rather than a 500 from a constraint violation:

| Rule | Constraint |
|---|---|
| `CompareAtPrice` null or strictly above `Price` | Phase 02 check constraint |
| `Price > 0` | |
| `Currency` and `DenominationCurrency` exactly three letters, uppercase | |
| `StockQuantity >= 0` | Phase 02 check constraint |
| `PercentOff` 1–100 | Phase 07 check constraint |
| `ExpiresAt` after `StartsAt` | Phase 07 check constraint |
| Item-scoped coupon has a `ProductId`; cart-scoped has none | Phase 07 check constraint |
| `Volume.Number` unique within a manga | Phase 02 unique index |
| Every `categoryId` exists and its `Kind` matches the product's | New rule — see below |

The last one has no schema constraint and needs one in code: putting a manga into the `steam` category would render it in the gift-card navigation. Reject with 422.

### A gift card cannot be `Unlimited`

Phase 09 backs gift cards with a finite pool of codes. `InventoryMode.Unlimited` on a gift card means it can sell without a code to deliver, which is the failure that phase exists to prevent.

Reject with 422 on create and on update. It is a one-line check and it closes a hole that would otherwise appear months later, in production, as customers paying for nothing.

### Order status transitions

`PUT /orders/{id}/status` consults the transition matrix from Phase 11. Illegal transitions are `422`, naming both states.

Two rules that are not obvious:

1. **Setting `Paid` calls `IPaymentConfirmationService.ConfirmAsync`**, it does not write `Status = Paid`. Phase 11 established one path to `Paid`, and that path allocates gift-card codes, appends the status event and cancels sibling payment intents. An admin endpoint that set the column directly would produce a paid order with nothing allocated.

   Since no payment intent exists for a manually paid order, `ConfirmAsync` needs an internal overload that takes an order id and an actor instead of a provider confirmation, writing the status event with `Source = admin`. That is a legitimate manual override — cash on delivery, a bank transfer, a support correction — and it should be recorded as one.

2. **Transitioning to `Cancelled` calls `IInventoryService.ReleaseAsync`** and, for a gift-card order, returns `Allocated` codes to the pool and voids `Delivered` ones. Both are idempotent, so a retried cancellation is safe.

Every transition appends an `OrderStatusEvent` with `Source = admin`.

### Admin actions are attributable

Every write records who did it. `ICurrentUser.Id` fills `StockMovement.PerformedByUserId` and `OrderStatusEvent.Source`, and every admin write logs at Information with the actor id, the entity id and the action.

There is no general audit table in this plan. That is a deliberate limit: the ledgers that matter — stock and order status — already record who, and a universal audit log is a feature that should be designed rather than smuggled in.

---

## Security

**This is the phase the brief's §13 is about.** Customers must not be able to create products, update products, delete products, change prices, change stock, change availability, manage categories, access dashboard statistics, or manage orders.

| Concern | This phase |
|---|---|
| Authentication | Every write requires a bearer token. |
| Authorization | `[Authorize(Roles = Roles.Admin)]` on every write action. **Reusing the existing mechanism** — no policies, no permissions, no second authorization system, exactly as the brief requires. |
| Role checks | `Roles.Customer` gets `403` with `ProblemDetails.Title == "Auth.Forbidden"` from the existing `ProblemDetailsAuthorizationResultHandler`. |
| Validation | A validator for every request DTO, mirroring every schema constraint. |
| Sensitive data | `AdminCouponDto` publishes eligibility fields — admin-only, and never reachable from `/cart/coupons`. No endpoint here returns a gift-card code. |
| Concurrency | `RowVersion` on product, coupon and order updates. |
| Rate limiting | The global policy. Admin endpoints are low-volume; a compromised admin account is not a rate-limiting problem. |

### The attribute trap, restated

`CatalogController` now has public reads and admin writes. If anyone adds `[AllowAnonymous]` or `[Authorize]` at class level, the per-action attributes stop meaning what they say.

Defend it with a test rather than a comment. Phase 16 does this across the whole API; do it here for this controller at minimum:

```csharp
public void CatalogController_HasNoClassLevelAuthorizationAttribute()
```

Reflect over the type and assert neither `AuthorizeAttribute` nor `AllowAnonymousAttribute` is declared on the class.

### The authorization matrix

Every endpoint in this phase, against three callers. This table **is** the test list:

| Endpoint | Anonymous | Customer | Admin |
|---|---|---|---|
| `POST /catalog/products` | 401 | **403** | 201 |
| `PUT /catalog/products/{id}` | 401 | **403** | 200 |
| `DELETE /catalog/products/{id}` | 401 | **403** | 204 |
| `PUT /catalog/products/{id}/inventory` | 401 | **403** | 200 |
| `GET /admin/products` | 401 | **403** | 200 |
| `POST /catalog/categories` | 401 | **403** | 201 |
| `PUT /catalog/categories/{id}` | 401 | **403** | 200 |
| `DELETE /catalog/categories/{id}` | 401 | **403** | 204 |
| `GET /admin/coupons` | 401 | **403** | 200 |
| `POST /admin/coupons` | 401 | **403** | 201 |
| `PUT /admin/coupons/{id}` | 401 | **403** | 200 |
| `DELETE /admin/coupons/{id}` | 401 | **403** | 204 |
| `PUT /orders/{id}/status` | 401 | **403** | 200 |
| `GET /admin/orders` | 401 | **403** | 200 |

Fourteen endpoints, forty-two assertions. Write all of them. This is the single most valuable test class in the plan — a missing attribute on one action is a customer who can set their own prices, and it is invisible in a diff.

Note that a customer calling `PUT /orders/{id}/status` on **their own** order must still get 403, not 200. Ownership does not grant status transitions.

---

## Frontend Contract

**Nothing consumes this.** There is no admin UI.

What is ready when one is built: `roleGuard`, `AuthService.isAdmin`, the `role` claim in the token, `/forbidden` as a landing page, and `errorInterceptor` passing 403 through for inline rendering.

Shape decisions made for that future UI:

- **`AdminProductDto` returns all translations**, not one resolved string, so a form can edit both languages side by side without a second request.
- **`rowVersion` is on every editable DTO**, so a form can round-trip it without the client tracking versions itself.
- **Create and update use the same polymorphic `kind` discriminator** as the read DTOs, so one TypeScript union covers reading and writing.
- **`GET /admin/products` mirrors the public query parameters** plus `status`, so an admin catalogue screen can reuse the storefront's filter components.

---

## Testing

### Unit tests

| Test | Asserts |
|---|---|
| `AdminCatalogueServiceTests.Create_DerivesSlugFromEnglishTitle` | |
| `AdminCatalogueServiceTests.Create_ArabicOnlyTitle_Requires ExplicitSlug` | 422, not a generated slug. |
| `AdminCatalogueServiceTests.Create_DuplicateSlug_Returns409` | No auto-suffix. |
| `AdminCatalogueServiceTests.Create_SlugOfSoftDeletedProduct_Succeeds` | The filtered index earning its keep. |
| `AdminCatalogueServiceTests.Create_MissingArabicTranslation_Returns422` | |
| `AdminCatalogueServiceTests.Create_GiftCardWithUnlimitedInventory_Returns422` | **The Phase 09 rule.** |
| `AdminCatalogueServiceTests.Create_MangaInGiftCardCategory_Returns422` | Kind mismatch. |
| `AdminCatalogueServiceTests.Create_CompareAtPriceBelowPrice_Returns422` | Validator, before the constraint. |
| `AdminCatalogueServiceTests.Update_StaleRowVersion_Returns409` | |
| `AdminCatalogueServiceTests.Update_CannotChangeKind` | The request has no `kind` field; a manga stays a manga. |
| `AdminCatalogueServiceTests.Update_PartialTranslations_LeavesOthersIntact` | |
| `AdminCatalogueServiceTests.Delete_SoftDeletesAndHidesFromPublicQuery` | |
| `AdminCatalogueServiceTests.Delete_GiftCardWithAvailableCodes_Returns409` | |
| `AdminCatalogueServiceTests.Delete_ProductWithOrders_Succeeds` | And the order's snapshot is unchanged. |
| `AdminCatalogueServiceTests.DeleteCategory_WithProducts_Returns409NamingCount` | |
| `AdminCatalogueServiceTests.DeleteCategory_Empty_Succeeds` | |
| `AdminOrderServiceTests.SetPaid_CallsConfirmationServiceNotTheColumn` | **Pins Phase 11's one-path rule.** |
| `AdminOrderServiceTests.SetCancelled_ReleasesStock` | |
| `AdminOrderServiceTests.IllegalTransition_Returns422NamingBothStates` | Drive the matrix. |
| `AdminOrderServiceTests.DeliveredOrder_AcceptsNoTransition` | |
| `AdminCouponServiceTests.ItemScopeWithoutProduct_Returns422` | Before the check constraint. |
| `AdminCouponServiceTests.AdminDtoIncludesEligibilityFields` | The deliberate asymmetry with `/cart/coupons`. |

### Integration tests

| Test | Asserts |
|---|---|
| `AdminAuthorizationTests.*` | **The full 14×3 matrix above.** One fact per cell, or a theory over the table. |
| `AdminAuthorizationTests.CustomerCannotChangeStatusOfOwnOrder` | 403 even with ownership. |
| `AdminAuthorizationTests.CatalogControllerHasNoClassLevelAuthAttribute` | Reflection. The regression guard. |
| `AdminCatalogueApiTests.CreatedProductAppearsInPublicCatalogue` | End to end, in both languages. |
| `AdminCatalogueApiTests.DeactivatedProduct_DisappearsFromPublicButNotFromAdminList` | The whole reason `GET /admin/products` exists. |
| `AdminCatalogueApiTests.ConcurrentUpdates_SecondGets409` | Two `PUT`s with the same `rowVersion`. |
| `AdminCatalogueApiTests.InventoryAdjustment_WritesStockMovementWithActor` | `PerformedByUserId` is the admin. |
| `AdminOrderApiTests.SetPaid_AllocatesGiftCardCodes` | Cross-phase, through the real service. |
| `AdminOrderApiTests.SetCancelled_RestoresStockExactly` | Ledger quantities, not order lines. |
| `AdminOrderApiTests.SetCancelledTwice_IsIdempotent` | |

### Edge cases

- Creating a product with no categories: allowed. It is reachable by slug and by search, just not by browsing.
- Updating a product to `isActive: false` while it sits in customers' carts: the lines stay visible, checkout refuses. Phase 06's rule.
- Deleting a product that is in someone's wishlist: succeeds; the entry drops from their listing and the row survives. Phase 10's rule.
- Deleting a coupon that has redemptions: soft delete; `CouponRedemption` uses restrict delete but soft delete is an UPDATE, so it succeeds and history survives.
- Setting stock on an `Unlimited` product: allowed, stored, ignored by `StockStatus.Derive`. Do not silently switch the mode.
- A slug of exactly 160 characters: accepted; 161 is 422.
- Changing a coupon's `percentOff` while a customer has it applied to a cart: the cart re-evaluates on the next read; if it is now worth less, Phase 08 refuses the order rather than charging more. That chain is worth an explicit cross-phase test.

---

## Acceptance Criteria

- [ ] Product create, update, soft delete, and inventory adjustment, all `[Authorize(Roles = Roles.Admin)]` **per action** on `CatalogController`.
- [ ] `CatalogController` has **no class-level** `[Authorize]` or `[AllowAnonymous]`, proved by a reflection test.
- [ ] Category create, update and delete, with deletion refused while products reference it.
- [ ] `AdminProductsController` at `/admin/products` for a listing that includes inactive products.
- [ ] `AdminCouponsController` at `/admin/coupons` with full CRUD and eligibility fields published.
- [ ] `PUT /orders/{id}/status` on `OrdersController`, narrowed to Admin by an action-level attribute.
- [ ] `GET /admin/orders`, separate from the customer's `GET /orders`.
- [ ] Slug derived from the English title, rejected rather than machine-generated when it would be empty, `409` on collision, never auto-suffixed.
- [ ] `RowVersion` required on product, coupon and order updates; mismatch is `409`.
- [ ] Create requires a translation for every language in `SupportedLanguages.All`; update may be partial.
- [ ] A gift-card product cannot be set to `InventoryMode.Unlimited`.
- [ ] A product's categories must match its `Kind`.
- [ ] Every Phase 02 and Phase 07 check constraint has a matching validator producing a 422.
- [ ] Setting `Paid` goes through `IPaymentConfirmationService`, never by writing the column.
- [ ] Setting `Cancelled` releases stock and returns gift-card codes, idempotently.
- [ ] Every admin write records the actor and logs at Information.
- [ ] **The 14×3 authorization matrix is implemented as tests and passes**, including a customer being refused on their own order's status.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 02 - catalogue entities and RowVersion.
  Phase 03 - CatalogController and the read DTOs these extend.
  Phase 05 - IInventoryService.AdjustAsync and ReleaseAsync.
  Phase 07 - Coupon and its constraints.
  Phase 08 - Order, OrderStatus, OrderStatusEvent.
  Phase 09 - the gift-card pool rules this enforces.
  Phase 11 - the transition matrix and IPaymentConfirmationService.

Blocks:
  Phase 14 (covers) - the upload endpoint sits beside these product writes
                      and reuses their authorization shape.

Can be implemented independently:
  No - it depends on more phases than anything except Phase 08. It can be
  SPLIT usefully, though: product and category CRUD need only 02, 03 and 05,
  and could ship before coupons and order management.
```
