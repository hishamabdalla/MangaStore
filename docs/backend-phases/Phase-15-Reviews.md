# Phase 15 — Reviews and Ratings

**Recommended branch:** `phase-15-reviews`

---

## Objective

Make `averageRating` and `ratingCount` mean something. Right now they are numbers Phase 04 invented and Phase 03 faithfully republishes — a `4.7` with nothing behind it.

This phase adds customer reviews, derives the aggregates from them, and keeps the storefront's star ratings honest.

---

## A note on scope

**This is the one phase in the plan with no frontend consumer waiting for it.**

The storefront renders `rating` and `ratingCount` on cards and detail pages, so the aggregate is consumed. But there is no review list, no review form, no star input, no `Review` interface in `core/catalog/models/`, and no service method for one. Nothing in `manga-store\src` reads or writes an individual review.

It was included at explicit request. It is worth saying plainly what that means:

- The endpoint contract here is **designed, not derived**. Every other phase matches a TypeScript interface that already exists; this one does not, so the shape is a judgement call that a future UI may want changed.
- It is the safest phase to defer, and the safest to revise later.
- It should be sequenced last among the feature phases, which it is.

The aggregate becoming real is the part that has immediate value. That much is worth doing whatever happens to the review UI.

---

## Current State

`Product.AverageRating` (`decimal(2,1)`) and `Product.RatingCount` (`int`) exist from Phase 02, described in the guideline as "denormalised". Phase 04 seeds them with plausible fiction — `4.7` with `1284` ratings — transcribed from the frontend's sample data.

There is no `Review` table, no way to submit one, and nothing that recomputes the aggregate.

---

## Scope

| Component | Files |
|---|---|
| Domain | `Review`, `ReviewStatus`, `IReviewRepository` |
| Application | `Features/Reviews/` — `ReviewDto`, `ReviewSummaryDto`, `CreateReviewRequest`, `UpdateReviewRequest`, validators, `ReviewProfile`, `IReviewService` / `ReviewService` |
| Infrastructure | Configuration, repository, the aggregate recomputation, migration, seeder extension |
| API | Review actions on `CatalogController`; a moderation action for admins |

### Out of scope

- Review images, replies, and helpfulness voting. None is requested and each is its own feature.
- Automated moderation or profanity filtering. Manual hide is the moderation tool.
- Review-request emails. `IEmailSender` has one method and logs to the console.

---

## Database Changes

### `Review`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity` |
| `ProductId` | `uniqueidentifier` | FK, restrict delete |
| `UserId` | `uniqueidentifier` | Bare, as elsewhere |
| `Rating` | `int` | 1–5. Check constraint |
| `Title` | `nvarchar(120)` NULL | |
| `Body` | `nvarchar(2000)` NULL | |
| `Status` | `int` | `ReviewStatus { Published, Hidden }` |
| `HiddenReason` | `nvarchar(200)` NULL | Admin-only |
| `OrderId` | `uniqueidentifier` NULL | The purchase that entitled the review. FK, restrict delete |
| `RowVersion` | `rowversion` | |

**Unique filtered on `(ProductId, UserId)`** — one review per customer per product. Editing replaces; it does not accumulate.

Indexes: `(ProductId, Status, CreatedAt DESC)` for the public listing.

**No `AuthorName` column.** The display name comes from `IIdentityService` at read time, so a customer who changes their display name changes it everywhere, including on reviews they have already left. Snapshotting it would freeze a name someone may have had a reason to change.

`Review` is **not** in one language. It is what a customer wrote, in whatever language they wrote it. No translation table, and no attempt to translate it.

### Migration

```bash
dotnet ef migrations add AddReviews \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

---

## API Contract

Review reads are public; writes require a signed-in customer; moderation requires an admin. Same per-action attribute discipline as the rest of `CatalogController`.

### `GET /catalog/products/{slug}/reviews`

| | |
|---|---|
| Auth | `[AllowAnonymous]` |
| Request | `pageNumber`, `pageSize`, `sort` (`newest` default, `highest`, `lowest`) |
| Success | `200` `PaginatedList<ReviewDto>` |
| Errors | `404` when the slug is unknown |

`Published` reviews only. `ReviewDto` is `{ id, rating, title, body, authorDisplayName, verifiedPurchase, createdAt, updatedAt }`.

**No `userId`, no email.** A public listing that carries a user id is a customer-enumeration surface.

### `POST /catalog/products/{id}/reviews`

| | |
|---|---|
| Auth | `[Authorize]` |
| Request | `CreateReviewRequest { int Rating, string? Title, string? Body }` |
| Success | `201` `ReviewDto` + `Location` |
| Errors | `401`, `403` `Review.Forbidden` (no qualifying purchase), `404`, `409` already reviewed, `422` |

### `PUT /catalog/products/{id}/reviews/mine` · `DELETE /catalog/products/{id}/reviews/mine`

| | |
|---|---|
| Auth | `[Authorize]` |
| Success | `200` `ReviewDto` / `204` |
| Errors | `401`, `404` |

`/mine` rather than `/{reviewId}` for the same reason the cart takes no cart id: the caller can only ever act on their own review, so there is no id to accept and no ownership check to forget.

### `PUT /catalog/reviews/{id}/moderation`

| | |
|---|---|
| Auth | `[Authorize(Roles = Roles.Admin)]` |
| Request | `ModerateReviewRequest { ReviewStatus Status, string? Reason }` |
| Success | `200` |
| Errors | `401`, `403`, `404`, `422` |

Hiding is reversible and recorded. There is no admin delete — a hidden review can be reinstated if the moderation was wrong, and a deleted one cannot.

---

## Business Rules

### Only purchasers may review

A customer may review a product only if they have an order containing it with status `Paid`, `Shipped` or `Delivered`. Otherwise `403` `Review.Forbidden`.

This is the strictest of the reasonable options and it is chosen deliberately:

| Option | Trade-off |
|---|---|
| **Purchase required** *(chosen)* | Review spam is structurally impossible. Fewer reviews. Every review is genuine |
| Any signed-in user, flagged `verifiedPurchase` | More reviews, and a moderation problem the moment the shop is worth targeting |
| Anonymous | Not viable. There is nothing to rate-limit and nothing to hold accountable |

The shop has no moderation staff and no automated filtering. A rule that makes abuse impossible beats one that makes it detectable.

`Review.OrderId` records the qualifying order, so `verifiedPurchase` is always `true` today. The field exists anyway, because the day the rule is relaxed it needs to already be on the wire rather than being a breaking change.

> **Note the interaction with Phase 11.** No order reaches `Paid` until a real cashier exists. Until then, **no customer can leave a review**, and the only reviews in the system are the seeded ones. That is correct, honest, and a good reason not to hurry this phase.

### Aggregates are recomputed, never incremented

The obvious implementation is wrong for the same reason Phase 05's stock decrement is:

```csharp
// WRONG. Two concurrent reviews both read the old count and both write count + 1.
product.RatingCount += 1;
product.AverageRating = (old * oldCount + rating) / (oldCount + 1);
```

Recompute from the source, in one statement, inside the same transaction as the review write:

```sql
UPDATE p SET
    p.RatingCount   = ISNULL(r.Cnt, 0),
    p.AverageRating = ISNULL(r.Avg, 0),
    p.UpdatedAt     = @now
FROM Products p
OUTER APPLY (
    SELECT COUNT(*) AS Cnt, ROUND(AVG(CAST(Rating AS decimal(4,2))), 1) AS Avg
    FROM Reviews
    WHERE ProductId = p.Id AND Status = 0 AND IsDeleted = 0
) r
WHERE p.Id = @productId;
```

Expressed through `ExecuteUpdateAsync` where EF can, or `ExecuteSqlInterpolatedAsync` where it cannot. Either way:

- It is idempotent — running it twice gives the same answer.
- It cannot drift, because it never reads its own previous output.
- `UpdatedAt` is set explicitly, since these APIs bypass the audit interceptor.

Recompute on every event that changes the set: create, update, delete, hide, unhide.

`AverageRating` is `decimal(2,1)` — one decimal place, `0.0` to `5.0`. Round half away from zero, consistent with `Money.Round`, so `4.25` becomes `4.3`.

A product with no published reviews has `RatingCount = 0` and `AverageRating = 0`. The client renders `0` stars with `(0)` beside it, which is the honest display for something nobody has rated.

### The seeded ratings have to go somewhere

Phase 04 seeds `AverageRating` and `RatingCount` directly, with no reviews behind them. Once this phase makes the aggregate derived, those numbers are inconsistent with their own source — and the first real review on a product with a seeded `4.7 (1284)` would recompute it to `5.0 (1)`, a jump nobody can explain.

Two options, and only one is coherent:

| Option | Result |
|---|---|
| Recompute every product from reviews at deploy | Every seeded rating becomes `0.0 (0)`. Correct, and it strips the stars off the whole demo storefront |
| **Seed reviews instead of aggregates** *(chosen)* | Extend Phase 04's seeder to write reviews, then recompute. The aggregates match their source and the storefront keeps its stars |

So this phase **changes the seeder**: for each seeded product, generate reviews that produce approximately the sample's rating and a plausible count, then run the recomputation. Cap the seeded count at something the table can hold sensibly — a few dozen per product rather than the sample's `1284` — and accept that `ratingCount` on a demo dataset is smaller than the fiction it replaces. A smaller true number is better than a large false one.

Seeded reviews need a seeded author, and every customer account is created by registration. Seed a small set of demo customer accounts through `IIdentityService` alongside them, or attach the reviews to the seeded admin — the first is more realistic and the second is less work. Either is defensible; say which was chosen.

### Editing and deleting

`PUT .../reviews/mine` replaces rating, title and body, sets `UpdatedAt`, and recomputes. A hidden review edited by its author stays hidden — editing is not a way to escape moderation.

`DELETE .../reviews/mine` soft-deletes and recomputes. The filtered unique index means the customer can then review the product again, which is the right behaviour for "I want to start over".

### Hidden reviews

Excluded from the public listing and from the aggregate. Visible to their author, with their status, so the customer is not left wondering where their review went.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Reads anonymous; writes `[Authorize]`; moderation `[Authorize(Roles = Roles.Admin)]`. All per action. |
| Authorization | `/mine` routes make ownership structural. Moderation is role-gated. |
| Validation | `Rating` 1–5; `Title` at most 120; `Body` at most 2000; at least one of rating alone or rating plus text — a review with no rating is not a review. |
| Sensitive data | The public listing carries a display name and nothing else. **No user id, no email, no order reference.** |
| Concurrency | Unique index for double submission; `RowVersion` for edits; recomputation is idempotent. |
| Rate limiting | The purchase requirement is the real limit. The global policy covers the rest. |

### Stored content is the risk

A review body is customer-supplied text that is shown to other customers. That is a stored cross-site scripting vector if anything renders it as HTML.

- **Store it as written.** Do not strip tags server-side — sanitising on write means the stored data no longer matches what the customer typed, and a second consumer with different escaping still has the problem.
- **The API returns JSON**, and JSON encoding is not HTML encoding. `<script>` in a body is returned verbatim and correctly.
- **The client must render it as text**, never with `innerHTML` or `[innerHTML]`. Angular escapes interpolation by default, so `{{ review.body }}` is safe and is what the future UI must use. Write that requirement down where the frontend work will see it.
- **Reject control characters** other than newline and tab, and cap length. Neither is a security control on its own; both keep the data sane.

### What is not exposed

- No endpoint lists a user's reviews across products. That would be a purchase-history disclosure.
- No endpoint returns `Review.UserId` or `Review.OrderId` to anyone but an admin.
- `HiddenReason` is admin-only and never in the public DTO.

---

## Frontend Contract

**Nothing consumes this**, beyond the aggregates that are already rendered.

What changes without any frontend work: `rating` and `ratingCount` become derived from real data. On a seeded environment the stars stay populated because the seeder now writes reviews; on a fresh production environment they are `0.0 (0)` until customers review, which is correct.

If a review UI is built later, it needs — and this phase's DTOs supply — a paginated list under the product detail page, a star input gated on `AuthService.isAuthenticated`, and a 403 path explaining that only purchasers may review. New TypeScript interfaces, new translation keys, and `{{ }}` interpolation for the body, never `[innerHTML]`.

---

## Testing

### Unit tests

| Test | Asserts |
|---|---|
| `ReviewServiceTests.Create_WithoutQualifyingOrder_Returns403` | The core rule. |
| `ReviewServiceTests.Create_WithPendingOrderOnly_Returns403` | `Pending` does not qualify — the customer has not paid. |
| `ReviewServiceTests.Create_WithDeliveredOrder_Succeeds` | |
| `ReviewServiceTests.Create_Twice_Returns409` | |
| `ReviewServiceTests.Create_AfterDeletingOwnReview_Succeeds` | The filtered index. |
| `ReviewServiceTests.Create_RatingOutOfRange_Returns422` | 0 and 6. |
| `ReviewServiceTests.Update_KeepsHiddenStatus` | Editing does not escape moderation. |
| `ReviewServiceTests.Delete_RecomputesAggregate` | |
| `ReviewServiceTests.Aggregate_ExcludesHiddenAndDeleted` | |
| `ReviewServiceTests.Aggregate_NoReviews_IsZeroNotNull` | |
| `ReviewServiceTests.Aggregate_RoundsHalfAwayFromZero` | 4.25 → 4.3. |
| `ReviewServiceTests.Aggregate_IsRecomputedNotIncremented` | Assert the recompute call, not an arithmetic update. **The concurrency-correctness test.** |
| `ReviewServiceTests.PublicDtoOmitsUserIdAndOrderId` | Serialise and count members. |
| `ReviewServiceTests.AuthorNameComesFromIdentityService` | Not from a stored column. |

### Integration tests

| Test | Asserts |
|---|---|
| `ReviewApiTests.List_IsAnonymous` | No token, 200. |
| `ReviewApiTests.List_ExcludesHidden` | |
| `ReviewApiTests.Create_RequiresAuthentication` | |
| `ReviewApiTests.Moderate_RequiresAdmin` | Customer → 403 `Auth.Forbidden`. |
| `ReviewApiTests.HidingReview_LowersProductRating` | End to end through the catalogue endpoint. **The cross-phase test.** |
| `ReviewApiTests.ScriptInBodyIsReturnedVerbatimAsJson` | Not stripped, not double-encoded. Pins the "escape at render, not at write" decision. |
| `ReviewApiTests.SeededProductsHaveMatchingAggregateAndReviewCount` | The seeder change, verified. |
| `ReviewApiTests.ListResponseContainsNoUserIdOrEmail` | Raw JSON sweep. |

### Concurrency

Mark `[Trait("Category", "SqlServer")]`, as in Phases 05 and 08:

```csharp
public async Task TenConcurrentReviews_ProduceExactlyTenAndACorrectAverage()
```

Ten customers with qualifying orders review one product simultaneously. Assert `RatingCount == 10` and an average matching the arithmetic mean to one decimal place. An incrementing implementation fails this; a recomputing one cannot.

### Edge cases

- A review on a soft-deleted product: `404`, and the existing review stops appearing anywhere.
- A rating with no title or body: valid.
- A body of exactly 2000 characters: valid; 2001 is 422.
- A customer whose account is deleted: the review remains and `authorDisplayName` is null or a placeholder, not an exception.
- All reviews for a product hidden: `0.0 (0)`, not the last non-hidden value.
- A review for a product bought in a cancelled order: `403`. The purchase did not stand.

---

## Acceptance Criteria

- [ ] `Review` entity, configuration, repository and `DbSet`; migration `AddReviews`.
- [ ] Unique filtered index on `(ProductId, UserId)`; check constraint `Rating BETWEEN 1 AND 5`.
- [ ] No `AuthorName` column — the display name is resolved through `IIdentityService`.
- [ ] Public list, own-review create/update/delete under `/mine`, and admin moderation, with per-action authorization attributes.
- [ ] **Only customers with a `Paid`, `Shipped` or `Delivered` order containing the product may review**; otherwise 403 `Review.Forbidden`.
- [ ] Aggregates **recomputed** from published reviews in one statement, inside the write's transaction, on every event that changes the set.
- [ ] `AverageRating` is one decimal place, rounded half away from zero; no reviews gives `0.0 (0)`.
- [ ] Phase 04's seeder extended to seed reviews and recompute, so seeded aggregates match their source.
- [ ] Hidden reviews excluded from the listing and the aggregate, visible to their author.
- [ ] Public DTO carries no `userId`, no email, no `orderId`, no `hiddenReason`.
- [ ] Review text stored as written and returned verbatim; the "render as text, never `innerHTML`" requirement is documented for the frontend.
- [ ] **The SQL Server concurrency test passes**: ten simultaneous reviews give a count of ten and a correct average.
- [ ] The PR states that this phase has no frontend consumer and that its endpoint shape is a design decision open to revision.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 02 - Product.AverageRating and RatingCount.
  Phase 03 - the catalogue projection that publishes them.
  Phase 04 - the seeder this phase changes.
  Phase 08 - Order, for the purchase requirement.
  Phase 12 - IIdentityService display-name resolution (extends the batch
             lookup added there).

Blocks:
  Nothing.

Can be implemented independently:
  No, but it is the most deferrable phase in the plan. Nothing depends on it,
  no frontend consumes it, and the purchase requirement means it produces no
  customer reviews until a real cashier exists. Sequence it last.
```
