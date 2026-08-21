# Phase 10 — Wishlist

**Recommended branch:** `phase-10-wishlist`

---

## Objective

Persist saved products against the user instead of the browser. One entity, three endpoints, no pricing and no side effects.

This is the smallest phase in the plan and the only one with no interaction with money, stock or transactions. It is a good candidate to run in parallel with anything else.

---

## Current State

### Backend

Nothing. No `WishlistItem`, no endpoint.

### Frontend

`manga-store\src\app\core\catalog\wishlist.service.ts` is an abstract class bound to `InMemoryWishlistService`. It stores `[{ productId, savedOn }]` under the `localStorage` key `mangastore.wishlist` and re-resolves products through `CatalogService` on load — the same pattern the cart uses, and for the same reason: ids survive, prices do not.

The control is a **toggle** — a heart on the product card and on the detail page — which is why the guideline specifies an idempotent `PUT` rather than a `POST`.

`/account/wishlist` renders the saved products as cards. `WishlistEntry` is `{ product: ProductSummary, savedOn: string }`.

---

## Scope

| Component | Files |
|---|---|
| Domain | `WishlistItem`, `IWishlistRepository` |
| Application | `Features/Wishlist/` — `WishlistItemDto`, `WishlistProfile`, `IWishlistService` / `WishlistService` |
| Infrastructure | `WishlistItemConfiguration`, `WishlistRepository`, migration |
| API | `WishlistController` |

### Out of scope

No merge-on-sign-in endpoint. A wishlist is not a cart: losing an anonymous wishlist on sign-in costs a customer a few clicks, not a purchase, and every merge endpoint is a surface with rules that have to be right. If it is wanted later it is `POST /wishlist/merge` and it is trivial — but it is not wanted now, and the frontend does not call it.

No notification when a saved product comes back in stock. There is no email sender worth the name and no UI for it.

---

## Database Changes

### `WishlistItem`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `UserId` | `uniqueidentifier` | Bare, as `RefreshToken` and `Cart` do |
| `ProductId` | `uniqueidentifier` | FK, restrict delete |

**Unique filtered on `(UserId, ProductId)`** where `IsDeleted = 0`. That index is what makes the idempotent `PUT` safe under a double click.

`SavedOn` is **not** a column — `BaseEntity.CreatedAt` already is it, maintained by `AuditInterceptor`. The DTO maps `CreatedAt` to `savedOn`.

> **`productId`, not `mangaId`.** The guideline specifies `PUT /wishlist/{mangaId}`. That wording predates the multi-product refactor; the frontend's TypeScript uses `productId` throughout, and a wishlist keyed on `mangaId` could not hold a gift card. Route it as `{productId}`. Phase 06 carries the same correction for the cart.

Index `(UserId, CreatedAt DESC)` for the listing order.

### Migration

```bash
dotnet ef migrations add AddWishlist \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

---

## API Contract

`WishlistController : ApiControllerBase`, class-level `[Authorize]`. No anonymous action, so the class-level attribute is safe here.

### `GET /wishlist`

| | |
|---|---|
| Success | `200` `WishlistItemDto[]` |
| Errors | `401` |

A bare array, newest first, not paginated. A wishlist is a handful of items and the page renders all of them; a paginated wishlist would be a paginator the UI has no room for.

Cap it at 200 items server-side and stop accepting additions beyond that (see below), so "not paginated" stays true.

### `PUT /wishlist/{productId}`

| | |
|---|---|
| Success | `204` |
| Errors | `401`, `404` `Product.NotFound`, `409` when the wishlist is full |

**Idempotent on purpose**: saving an already-saved product succeeds rather than conflicting, because the client's control is a toggle and a toggle that errors on its own state is broken. The `CreatedAt` of the existing row is left alone — re-saving does not move the item to the top of the list.

`PUT` rather than `POST` for the same reason: the request describes a desired end state, and repeating it is harmless.

### `DELETE /wishlist/{productId}`

| | |
|---|---|
| Success | `204` |
| Errors | `401` |

Also idempotent. Removing something that is not there succeeds — **not `404`**. The toggle can be clicked twice, and the second click's desired state is already true.

> This is the one place the plan deliberately breaks the usual "delete something that does not exist is a 404" convention. It is a toggle, both directions, and both directions should behave the same way. Say so in the action's XML doc.

### `WishlistItemDto`

```jsonc
{
  "product": { /* ProductSummaryDto, polymorphic on kind */ },
  "savedOn": "2026-08-21T14:03:11.482Z"
}
```

Matches `WishlistEntry` in `models/order.model.ts` exactly. The nested product is the same polymorphic summary Phase 03 defines and Phase 06 nests in cart lines, with the same include chain and `.AsSplitQuery()`.

---

## Business Rules

### Withdrawn products are dropped from the listing

`GET /wishlist` filters nested products by `IsActive = true`; the soft-delete query filter removes deleted ones. A saved product that has been withdrawn simply does not appear.

This differs from the cart, where a withdrawn line **is** shown so the customer can see why checkout refuses. The reasoning differs with it: a cart line the customer cannot buy needs an explanation, whereas a wishlist entry that quietly disappears costs nothing and explaining it would mean rendering a card for something that no longer exists.

The row is **not deleted** — it stays, and the item reappears if the product is reactivated. Reactivation is a normal admin action, and deleting the row would silently punish customers for it.

### Saving an inactive product fails

`PUT` for a product that is not in the public catalogue is `404`. It is not visible, so as far as a customer is concerned there is nothing to save.

The asymmetry with the listing rule is deliberate: an existing save survives a withdrawal, a new one cannot be created for something already withdrawn.

### The cap

200 items. Beyond that, `PUT` returns `409` `Wishlist.Conflict`. Nobody has a wishlist that long, and the cap is what lets the endpoint stay unpaginated without an unbounded response.

### Ownership

Every query is scoped by `ICurrentUser.Id`. The wishlist item id never appears in a route — the route key is the **product** id, which the caller already knows. There is nothing to enumerate and no ownership check to forget.

### Change tracking

`PUT` and `DELETE` both write. `DELETE` uses `ExecuteDeleteAsync` scoped by `(UserId, ProductId)` — one statement, no load, and the soft-delete interceptor is bypassed.

That last part is a decision, not an oversight: **a removed wishlist entry is hard-deleted.** Soft-deleting it would leave a row that the unique filtered index ignores, which is correct, but the table would accumulate one dead row per un-save forever, for data with no audit value whatsoever. Note it in the configuration so the departure from the soft-delete norm is visible.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Class-level `[Authorize]`. |
| Authorization | Ownership by construction — user id from the token, product id from the route, no wishlist id anywhere. |
| Role checks | None. |
| Validation | The route parameter is `Guid`-typed, so a malformed id is a routing 404 before any code runs. No request body on any action. |
| Sensitive data | A wishlist reveals interest, which is mild but real. Scoped to the owner and never listed across users. |
| Concurrency | The unique filtered index handles a double-click `PUT`; catch the constraint violation and return `204`, since the desired state was reached. |
| Rate limiting | The global policy. A toggle is chatty and legitimate. |

Nothing here is high risk. The one thing to avoid is adding a "who else saved this?" count to the product DTO — it is a tempting engagement metric and it turns a private list into a public signal.

---

## Frontend Contract

| Frontend method | Endpoint |
|---|---|
| `WishlistService.restore()` | `GET /wishlist` |
| `WishlistService.toggle(product)` | `PUT` or `DELETE /wishlist/{productId}` |
| `WishlistService.remove(productId)` | `DELETE /wishlist/{productId}` |

Swap `{ provide: WishlistService, useClass: HttpWishlistService }` in `app.config.ts`, then delete `in-memory-wishlist.service.ts` and the `mangastore.wishlist` storage key.

Two changes the frontend has to absorb:

1. **The wishlist becomes sign-in-gated.** Today an anonymous visitor can save items; afterwards they cannot, because the endpoint is `[Authorize]`. The heart control needs to either prompt for sign-in or stay local while signed out. The simplest honest behaviour, and the one that matches the cart: keep `InMemoryWishlistService` for anonymous visitors and switch to the HTTP one on sign-in. Without a merge endpoint the local list is dropped at that point, which is acceptable — but it should be a deliberate drop, not a silent one.

2. **Mutations become asynchronous.** `toggle()` currently returns `void`. Optimistic update then reconcile, as with the cart.

`savedOn` is a full UTC timestamp with a `Z`, per Phase 01.

---

## Testing

### Unit tests

| Test | Asserts |
|---|---|
| `WishlistServiceTests.Put_NewProduct_Creates` | |
| `WishlistServiceTests.Put_AlreadySaved_Succeeds` | `204`, one row, **`CreatedAt` unchanged**. |
| `WishlistServiceTests.Put_InactiveProduct_Returns404` | |
| `WishlistServiceTests.Put_UnknownProduct_Returns404` | |
| `WishlistServiceTests.Put_AtCap_Returns409` | 200 saved, the 201st refused. |
| `WishlistServiceTests.Delete_NotSaved_Succeeds` | **`204`, not `404`.** The toggle rule. |
| `WishlistServiceTests.Get_ExcludesInactiveProductsButKeepsRows` | Deactivate, list is empty; reactivate, item returns. |
| `WishlistServiceTests.Get_IsNewestFirst` | |
| `WishlistServiceTests.Anonymous_IsUnauthorized` | All three methods. |

### Integration tests

| Test | Asserts |
|---|---|
| `WishlistApiTests.AllEndpoints_RequireAuthentication` | |
| `WishlistApiTests.WishlistIsScopedToCaller` | User A saves; user B's list is empty. |
| `WishlistApiTests.PutTwice_LeavesOneRow` | Under sequential and concurrent calls. |
| `WishlistApiTests.NestedProduct_IsPolymorphicOnKind` | A saved gift card carries `denomination`. |
| `WishlistApiTests.SavedOn_CarriesZDesignator` | Phase 01's converter, again. |
| `WishlistApiTests.DeleteHardDeletesRow` | Query with `IgnoreQueryFilters` and find nothing. Pins the deliberate soft-delete departure. |

### Edge cases

- `PUT` and `DELETE` for the same product simultaneously: last write wins, and either end state is legitimate. The client re-reads.
- A product deleted while it is on someone's wishlist: restrict-delete on the FK means a hard delete is refused; soft delete is what happens in practice and the item drops from the listing.
- A wishlist of exactly 200 with one item removed then another added: succeeds.
- A malformed `Guid` in the route: framework 404, no code runs.

---

## Acceptance Criteria

- [ ] `WishlistItem` entity, configuration, repository and `DbSet`; migration `AddWishlist`.
- [ ] Unique filtered index on `(UserId, ProductId)`; listing index on `(UserId, CreatedAt DESC)`.
- [ ] No `SavedOn` column — `CreatedAt` is mapped to `savedOn` in the profile.
- [ ] `WishlistController` with three actions, class-level `[Authorize]`, each one line.
- [ ] `PUT` is idempotent and does not move an existing item's `CreatedAt`.
- [ ] `DELETE` is idempotent and returns `204` for an unsaved product, with the deviation documented in its XML doc.
- [ ] `GET` returns a bare array, newest first, excluding inactive and deleted products while keeping their rows.
- [ ] Saving an inactive product is `404`; the asymmetry with the listing rule is documented.
- [ ] 200-item cap enforced with a `409`.
- [ ] Removal hard-deletes, with the departure from the soft-delete norm noted in the configuration.
- [ ] `WishlistItemDto` matches `WishlistEntry` exactly, nesting the polymorphic `ProductSummaryDto`.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 02 - Product.
  Phase 03 - ProductSummaryDto and its include chain.

Blocks:
  Nothing.

Can be implemented independently:
  Yes. Once Phases 02 and 03 are merged this needs nothing from 04-09 and
  nothing from 11-16. It is the best candidate in the plan for running in
  parallel with another phase, or for a first contribution.
```
