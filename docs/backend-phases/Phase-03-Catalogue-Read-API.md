# Phase 03 — Catalogue Read API

**Recommended branch:** `phase-03-catalogue-api`

---

## Objective

Put the catalogue on the wire. Five public, anonymous read endpoints that between them satisfy every method on the frontend's `CatalogService`.

This is the phase that pays. When it merges, `manga-store\src\app\app.config.ts` swaps one provider line from `InMemoryCatalogService` to `HttpCatalogService`, and the home page, the catalogue page and the product detail page all go live at once. Cart, wishlist and orders keep working on local data, because they persist ids and re-read products through the same service.

---

## Current State

### What exists after Phase 02

Sixteen catalogue tables, eight root entities, seven translation tables, five enums, `StockStatus.Derive`, and repository interfaces declared but with no query methods beyond what `IRepository<T>` gives.

### What exists after Phase 01

`JsonStringEnumConverter` with camelCase — without it every enum on these DTOs would serialise as an integer and the storefront would render `stock.0`. `IRequestLanguage` to resolve `Accept-Language`. `CommerceOptions.LowStockThreshold` for the derivation. `PaginatedList<T>` and `PaginationParams`, unchanged since the template.

### What is missing

No controller, no DTO, no AutoMapper profile, no validator, no service, and no way to express the catalogue query. `IRepository<T>` offers `GetPagedAsync(skip, take)` ordered by `CreatedAt` descending, and nothing else — no search, no filter, no sort.

There is also **no data**. Phase 04 seeds it. These endpoints will return empty pages until then, which is a perfectly good state to merge in: the contract is testable with data inserted by the tests themselves.

---

## Scope

| Component | Files |
|---|---|
| Domain | `ProductQuery`, `ProductSortOrder`; query methods on `IProductRepository` and `ICategoryRepository` |
| Application | `Features/Catalogue/` — DTOs, `CatalogueQueryParams` + validator, `CatalogueProfile`, `ICatalogueService` / `CatalogueService` |
| Infrastructure | `ProductRepository` and `CategoryRepository` query implementations |
| API | `CatalogController` |

### Out of scope

Writes of any kind. Cover-image upload. Seed data. Anything a signed-in user does.

---

## Database Changes

**None.** Phase 02 created the schema. If a query here turns out to need an index Phase 02 did not create, add it in a small migration in this phase rather than editing Phase 02's — the drift gate in CI will catch a model change with no migration either way.

Two indexes from Phase 02 carry the weight here and are worth confirming exist:

- `ProductTranslations (LanguageCode, Title)` — the `titleAsc` sort and half the search.
- `AuthorTranslations (LanguageCode, Name)` — the other half.

---

## API Contract

All routes are under `/api/v1`. `CatalogController` inherits `ApiControllerBase`, which supplies `[ApiController]`, `[Route("api/v{version:apiVersion}/[controller]")]`, `[ApiVersion(1)]`, and the shared 401/500 `[ProducesResponseType]` declarations.

> **`[AllowAnonymous]` goes on each action, never on the class.** Phase 13 adds admin writes to this same controller, and `CLAUDE.md` names the trap explicitly: a class-level `[AllowAnonymous]` wins over an action-level `[Authorize]` and silently opens it. `CatalogController` mixing public reads with admin writes is exactly that shape.

> **Controller naming.** `CLAUDE.md` asks for plural nouns, which would give `ProductsController` at `/api/v1/products`. The guideline commits to `/catalog/products`, and those paths appear in shared storefront links. `Catalog` is a collection noun, so `CatalogController` satisfies the spirit of the rule; note the deviation in the class's XML doc so the next reader does not "fix" it.

### `GET /catalog/products`

| | |
|---|---|
| Auth | Anonymous |
| Request | `[FromQuery] CatalogueQueryParams` |
| Success | `200` `PaginatedList<ProductSummaryDto>` |
| Errors | `422` `ProblemDetails` (invalid query) |

Query parameters — **these names are already in shared storefront URLs and cannot change**:

| Parameter | Type | Default | Behaviour |
|---|---|---|---|
| `search` | `string?` | — | Case-insensitive; matches product title, author name, brand name and gift-card denomination, **in both languages regardless of `Accept-Language`** |
| `categorySlug` | `string?` | — | Single category |
| `minPrice` | `decimal?` | — | Inclusive |
| `maxPrice` | `decimal?` | — | Inclusive |
| `type` | `string?` | all | `manga` or `giftCard`. **Named `type`, not `kind`** |
| `inStockOnly` | `bool?` | `false` | Excludes `outOfStock` only — `lowStock` and `preOrder` still appear |
| `onSaleOnly` | `bool?` | `false` | Only products whose `CompareAtPrice` exceeds `Price` |
| `sort` | `string?` | `newest` | `newest`, `priceAsc`, `priceDesc`, `titleAsc`, `rating` |
| `pageNumber` | `int` | `1` | Clamped to ≥ 1 and to the last page |
| `pageSize` | `int` | `20` | Clamped 1–100. The storefront always sends `12` |

`CatalogueQueryParams` inherits `PaginationParams`, so `PageNumber`, `PageSize` and `Skip` come with their existing clamping behaviour for free.

> **The `type` / `kind` split is deliberate.** The TypeScript field is `kind` (`CatalogQuery.kind`), but `catalog.page.ts` writes `type=manga` into the URL, and the guideline commits to that name because it is in links people have already shared. Bind `type` and map it to `ProductKind` in the service.

### `GET /catalog/products/{slug}`

| | |
|---|---|
| Auth | Anonymous |
| Request | `slug` route segment |
| Success | `200` `ProductDetailDto` |
| Errors | `404` `ProblemDetails`, title `Product.NotFound` |

An inactive or soft-deleted product is a `404`, not a `403`. There is no public difference between "withdrawn" and "never existed".

### `GET /catalog/products/{slug}/related`

| | |
|---|---|
| Auth | Anonymous |
| Request | `slug`; `take` query, default `4`, clamped 1–12 |
| Success | `200` `ProductSummaryDto[]` — a bare array, not paginated |
| Errors | `404` `ProblemDetails` when the slug is unknown |

Not paginated, because the client asks for exactly four and renders them in a rail.

### `POST /catalog/products/resolve`

| | |
|---|---|
| Auth | Anonymous |
| Request | `ResolveProductsRequest { IReadOnlyList<Guid> Ids }` |
| Success | `200` `ProductSummaryDto[]` |
| Errors | `422` when `ids` is empty or exceeds 100 |

`POST` rather than `GET ?ids=a,b,c` because a full cart plus a full wishlist can be a long list and query strings have limits that vary by proxy. It is a read expressed as a `POST` — a lookup, not a creation — so it returns `200`, never `201`.

**Ids that do not resolve are silently omitted.** The cart and wishlist call this on every page load to re-read prices; a product that was withdrawn since the cart was saved should drop out of the cart, not fail the whole restore. `CartService.restore()` and `WishlistService.restore()` both depend on this.

> **Route ordering.** `products/resolve` and `products/{slug}` do not collide, because one is `POST` and the other is `GET`. If a `GET` variant is ever added, it must be declared before the `{slug}` route or ASP.NET will match `resolve` as a slug.

### `GET /catalog/categories`

| | |
|---|---|
| Auth | Anonymous |
| Request | — |
| Success | `200` `CategoryDto[]` |
| Errors | — |

Every category, both kinds, unpaginated. There are twelve.

### DTOs

`ProductSummaryDto` is polymorphic on `kind`, matching the client's discriminated union. System.Text.Json expresses this natively:

```csharp
/// <summary>Everything a product card renders, and no more.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MangaSummaryDto), "manga")]
[JsonDerivedType(typeof(GiftCardSummaryDto), "giftCard")]
public abstract record ProductSummaryDto
{
    /// <summary>Unique identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Public identifier used in URLs.</summary>
    public required string Slug { get; init; }

    /// <summary>Already localized for the requested language.</summary>
    public required string Title { get; init; }

    /// <summary>Categories this product is published in.</summary>
    public required IReadOnlyList<CategoryRefDto> Categories { get; init; }

    /// <summary>What the shop charges.</summary>
    public required decimal Price { get; init; }

    /// <summary>Pre-discount price, or null when not discounted.</summary>
    public decimal? CompareAtPrice { get; init; }

    /// <summary>ISO 4217 code for <see cref="Price"/>.</summary>
    public required string Currency { get; init; }

    /// <summary>Derived availability.</summary>
    public required StockStatus StockStatus { get; init; }

    /// <summary>Average rating out of five.</summary>
    public required decimal Rating { get; init; }

    /// <summary>Number of ratings behind <see cref="Rating"/>.</summary>
    public required int RatingCount { get; init; }

    /// <summary>Release date, as a calendar day.</summary>
    public required DateOnly ReleasedOn { get; init; }

    /// <summary>Relative URL of the cover image, or null to use generated artwork.</summary>
    public string? CoverImageUrl { get; init; }
}
```

`MangaSummaryDto` adds `Author` (`AuthorDto { Id, Name }`) and `VolumeCount`.
`GiftCardSummaryDto` adds `Brand` (`BrandDto { Id, Name, AccentToken }`), `Denomination` (`DenominationDto { Amount, Currency }`), `Region` and `DeliveryType`.

`MangaDetailDto : MangaSummaryDto` adds `Synopsis`, `Publisher`, `ReadingDirection`, `Isbn`, `AgeRating`, `Volumes[]`.
`GiftCardDetailDto : GiftCardSummaryDto` adds `Description`, `RedemptionSteps[]`, `Terms`.

`CategoryDto` is `{ Id, Slug, Name, Kind, ProductCount }`.
`CategoryRefDto` — the nested form on a product — is `{ Id, Slug, Name }`, matching what the client's `Category` interface needs inside `ProductBase.categories`.

> `[JsonPolymorphic]` writes the discriminator as the **first** property and forbids a separate `Kind` member on the base record — declaring one is a runtime error. Register `UseOneOfForPolymorphism()` in `AddSwaggerGen` so Swagger and Scalar describe the union rather than only the base type.

### Error responses

Unchanged from auth. `ApiControllerBase` maps `ResultError` codes to statuses; `ProblemDetails.Title` carries `{Entity}.{Code}`.

| Case | Status | Title |
|---|---|---|
| Unknown or withdrawn slug | 404 | `Product.NotFound` |
| Unknown category slug in a filter | 200, empty page | — |
| `minPrice` greater than `maxPrice` | 422 | `Validation` |
| `sort` not in the allowed set | 422 | `Validation` |
| `type` not `manga` or `giftCard` | 422 | `Validation` |
| `ids` empty or over 100 | 422 | `Validation` |

An unknown `categorySlug` returning an empty page rather than a 404 is deliberate: the filter is one facet of a search, and a search that matches nothing is a normal outcome. A 404 there would make the catalogue page render an error for a stale bookmark.

---

## Business Rules

### Only active, released, undeleted products are public

Every query in this phase filters `IsActive = true`. Soft-deleted rows are already invisible through the query filter. There is no query-string parameter to override either — the admin views in Phase 13 use different endpoints.

Pre-orders **are** public: `ReleasedOn` in the future gives `stockStatus: "preOrder"` and the product is listed and buyable. The seed contains three of them precisely so that path is visible.

### Search spans both languages

Someone browsing in Arabic still searches by the English title. The filter ignores `Accept-Language` entirely:

```csharp
query.Where(p =>
    p.Translations.Any(t => EF.Functions.Like(t.Title, pattern)) ||
    (p.Manga != null && p.Manga.Author.Translations.Any(t => EF.Functions.Like(t.Name, pattern))) ||
    (p.GiftCard != null && EF.Functions.Like(p.GiftCard.Brand.Name, pattern)))
```

Matching the gift-card denomination — so that searching `70` finds the `$70` card — is worth doing but should not be a string comparison against a `decimal` column. Parse the search term; if it is numeric, add `p.GiftCard.DenominationAmount == parsed` as an additional `OR`.

Use `EF.Functions.Like` with a `%term%` pattern rather than `Contains`, so the SQL is explicit. Case-insensitivity comes from the database collation, which is case-insensitive by default on SQL Server; do **not** call `ToLower()` on the column, which would make the index unusable.

### Sorting

| `sort` | Order |
|---|---|
| `newest` (default) | `ReleasedOn` descending, then `Id` descending as a tiebreak |
| `priceAsc` | `Price` ascending |
| `priceDesc` | `Price` descending |
| `titleAsc` | Translated title ascending, in the **requested** language |
| `rating` | `AverageRating` descending, then `RatingCount` descending |

Always append a deterministic tiebreak. Without one, two products with the same `ReleasedOn` can swap between page 1 and page 2 of the same result set, and an item disappears from the paginated view.

`titleAsc` orders by a correlated subquery into `ProductTranslations` for the requested language. The `(LanguageCode, Title)` index makes it viable.

> **Arabic collation is a known limitation.** SQL Server's default collation does not sort Arabic the way an Arabic reader expects. It is not a blocker — `titleAsc` is one of five sorts and not the default — but do not claim the sort is linguistically correct for `ar`. If it matters later, the fix is a collation on `ProductTranslations.Title`, not application-side sorting.

### Pagination clamps, it does not reject

`PaginationParams` already raises `pageNumber` below 1 to 1 and caps `pageSize` at 100. It does **not** clamp a page past the end — that has to happen in the service, because it needs the total count:

1. Build the filtered query.
2. `CountAsync()` → `totalCount`.
3. `totalPages = ceil(totalCount / pageSize)`.
4. Effective page = `Math.Clamp(requested, 1, Math.Max(totalPages, 1))`.
5. Page the query with `Skip((effective - 1) * pageSize).Take(pageSize)`.
6. Return `PaginatedList<T>.Create(items, totalCount, effective, pageSize)` — the **effective** page, so the client's paginator lands where the data is.

With `totalCount = 0`, `totalPages` is 0, the effective page clamps to 1, and the result is an empty page 1. The client's paginator handles that; a 404 would not.

This matters because the catalogue page keeps `page` in the URL. Narrowing a filter while on page 5 must not error.

### Localization resolution and fallback

Every localized field is resolved once, in the query, using `IRequestLanguage.Code`, falling back to `SupportedLanguages.Default`:

```csharp
Title = p.Translations.Where(t => t.LanguageCode == language).Select(t => t.Title).FirstOrDefault()
     ?? p.Translations.Where(t => t.LanguageCode == SupportedLanguages.Default).Select(t => t.Title).First()
```

The DTO carries **one string**, never an object. The language is a property of the request, not of the payload.

### Related products

Same `Kind`, sharing at least one category, excluding the product itself, active, ordered by `AverageRating` descending, `take` items. If fewer than `take` match, return what there is — do not pad from the wider catalogue. Four related manga is a nicety; four unrelated ones is noise.

### `Category.productCount`

A count of products in that category that are active and not soft-deleted. Computed per request, not stored:

```csharp
ProductCount = c.Products.Count(p => p.IsActive && !p.IsDeleted)
```

Twelve categories with a correlated count is one cheap query. It drives the genre tiles on the home page, so a category with zero products still appears — with a count of `0`, not omitted.

### Mapping and loading

`CLAUDE.md` forbids manual mapping, so `CatalogueProfile` owns the shape. AutoMapper cannot pick a destination subtype from a *value* (`Product.Kind`) the way it can from a source type, so use a type converter rather than `Include<>`:

```csharp
/// <summary>Maps a product to the summary shape matching its kind.</summary>
public sealed class ProductSummaryConverter : ITypeConverter<Product, ProductSummaryDto>
{
    /// <inheritdoc/>
    public ProductSummaryDto Convert(Product source, ProductSummaryDto destination, ResolutionContext context) =>
        source.Kind switch
        {
            ProductKind.Manga => context.Mapper.Map<MangaSummaryDto>(source),
            ProductKind.GiftCard => context.Mapper.Map<GiftCardSummaryDto>(source),
            _ => throw new InvalidOperationException($"Unmapped product kind {source.Kind}."),
        };
}
```

This is one of the few places a throw is right: reaching it means `Kind` holds a value with no detail row, which is a data-integrity failure, not an expected outcome. It surfaces as a 500 through `GlobalExceptionHandler`, which is the correct signal.

The repository must `Include` what the profile reads, or AutoMapper will map nulls into required fields:

```csharp
.Include(p => p.Translations)
.Include(p => p.Categories).ThenInclude(c => c.Translations)
.Include(p => p.Manga!).ThenInclude(m => m.Author).ThenInclude(a => a.Translations)
.Include(p => p.GiftCard!).ThenInclude(g => g.Brand)
.Include(p => p.GiftCard!).ThenInclude(g => g.Translations)
```

Add `.AsSplitQuery()`. A single query across four collection includes produces a cartesian explosion — twelve products times two translations times three categories times two category translations is a large result set for twelve cards.

> **Honest trade-off.** Loading entities and mapping in the service pulls translations for *every* language, not just the requested one. At twelve products per page and two languages that is fine. The alternative — `ProjectTo` over an `IQueryable` — is faster but requires the repository to return `IQueryable`, which would let EF Core leak into Application and break the dependency rule. If the catalogue ever grows enough for this to matter, the fix is a projection seam in Infrastructure that returns DTO-shaped read models, not a relaxation of the layering.

### Tracking

Every query in this phase is a read. `AppDbContext` is already `NoTrackingWithIdentityResolution` globally, so nothing here needs `.AsTracking()` — and nothing here should call it.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | None. All five endpoints are `[AllowAnonymous]`, per action. |
| Authorization | None yet. Phase 13 adds `[Authorize(Roles = Roles.Admin)]` writes to this controller. |
| Role checks | None. |
| Validation | `CatalogueQueryParamsValidator` and `ResolveProductsRequestValidator`, invoked through `IValidationService` as the first line of each service method. |
| Sensitive data | The catalogue is public by definition. The one rule: **never `Include` anything from Phase 09's gift-card code tables into these projections.** A sellable code reaching a public DTO is unrecoverable — it is spent the moment it is served. |
| Concurrency | Not applicable; reads only. |
| Rate limiting | The global `fixed` policy (100/min) covers these. No per-endpoint policy; a public catalogue is meant to be read. |

### Input hardening

- `search` is bound to a parameterised `LIKE`. Cap its length at 100 in the validator — an unbounded search term is a cheap way to make the database work hard.
- `%` and `_` inside `search` are `LIKE` wildcards. Escape them, or a search for `50%` scans differently than the user intended. Not a security hole, but a correctness one.
- `take` on `/related` is clamped 1–12 in the validator, not just documented. An unclamped `take=100000` is a trivially cheap denial of service.
- `ids` on `/resolve` is capped at 100, matching `PaginationParams.MaxPageSize`.
- `minPrice` and `maxPrice` are validated as non-negative, and `minPrice <= maxPrice`.

---

## Frontend Contract

This phase satisfies all six methods on `CatalogService` (`manga-store\src\app\core\catalog\catalog.service.ts`):

| Frontend method | Endpoint |
|---|---|
| `list(query)` | `GET /catalog/products` |
| `getBySlug(slug)` | `GET /catalog/products/{slug}` |
| `categories()` | `GET /catalog/categories` |
| `related(slug, take)` | `GET /catalog/products/{slug}/related?take=` |
| `newArrivals(take)` | `GET /catalog/products?sort=newest&pageSize={take}` |
| `byIds(ids)` | `POST /catalog/products/resolve` |

The swap is one line in `manga-store\src\app\app.config.ts`:

```ts
{ provide: CatalogService, useClass: HttpCatalogService },
```

After it, `catalog.seed.ts`, `gift-card.seed.ts`, `product-images.ts` and `in-memory-catalog.service.ts` can be deleted.

### The localization shape change — plan for it

The frontend's TypeScript models type `title`, `name`, `synopsis`, `region`, `publisher` and `terms` as `LocalizedText` (`{ en: string; ar: string }`), because the in-memory services hold both languages and components call `localize(text, language)`.

**These endpoints return one already-resolved string.** That is the guideline's own recommendation (§5.1) and the shape its `ProductSummaryDto` example shows, and it is the right call — the language is a property of the request, the payload does not double in size, and adding a third language needs no client change.

But it means `HttpCatalogService` cannot be a drop-in for `InMemoryCatalogService` without a corresponding frontend change:

- `ProductBase.title`, `Category.name`, `Author.name`, `Volume.title`, `MangaDetail.synopsis`, `MangaDetail.publisher`, `GiftCardSummary.region`, `GiftCardDetail.description`, `GiftCardDetail.terms`, `GiftCardDetail.redemptionSteps` all become `string` / `readonly string[]`.
- Every `localize(x, language)` call site on those fields drops away.
- `LanguageService` must re-fetch catalogue data when the language changes, since the payload is now language-specific. Today it does not need to.
- `localized-text.ts` survives only if something else still needs it.

**Do not paper over this by returning `{ en, ar }`.** That pushes the choice into the client, doubles every payload, and makes a third language a client change. Flag it in the PR, and treat the frontend edit as part of the swap rather than a surprise discovered at runtime.

### Other things that must match exactly

- The pagination envelope: `items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`, `hasPreviousPage`, `hasNextPage`. `paged-result.ts` mirrors `PaginatedList<T>` field for field.
- `stockStatus` as a camelCase string. `'stock.' + stockStatus` is a translation key.
- `compareAtPrice` omitted or `null` when there is no discount — never equal to or below `price`.
- `releasedOn` as `YYYY-MM-DD`. `DateOnly` serialises this way by default; do not let it become a full timestamp.
- `coverImageUrl` absent is fine and expected until Phase 14 — `shared/ui/product-art.ts` falls back to generated artwork when it is missing *or* when the image fails to load.

---

## Testing

### Unit tests (`MangaStore.UnitTests`)

Substitute `IProductRepository`, `ICategoryRepository`, `IRequestLanguage` and `IValidationService` with NSubstitute; assert with Shouldly.

| Test | Asserts |
|---|---|
| `CatalogueServiceTests.List_PageBeyondEnd_ClampsToLastPage` | 25 items, `pageSize` 12, `pageNumber` 9 → returns page 3, and `PaginatedList.PageNumber` is 3, not 9. |
| `CatalogueServiceTests.List_EmptyResult_ReturnsPageOne` | `totalCount` 0 → `PageNumber` 1, `Items` empty, `TotalPages` 0. |
| `CatalogueServiceTests.List_InvalidSort_Returns422` | Short-circuits on validation; the repository is never called. |
| `CatalogueServiceTests.List_MinPriceAboveMaxPrice_Returns422` | Same. |
| `CatalogueServiceTests.GetBySlug_Unknown_ReturnsNotFound` | `ResultError.NotFound` with title `Product.NotFound`. |
| `CatalogueServiceTests.Resolve_UnknownIds_AreOmittedNotFailed` | Three ids in, one known → one item out, `IsSuccess` true. |
| `CatalogueServiceTests.Resolve_OverHundredIds_Returns422` | The cap is enforced. |
| `CatalogueProfileTests.Manga_MapsToMangaSummaryDto` | The type converter picks the right subtype. |
| `CatalogueProfileTests.GiftCard_MapsDenominationSeparatelyFromPrice` | `denomination.amount` 70 / `denomination.currency` USD alongside `price` 4025 / `currency` EGP. |
| `CatalogueProfileTests.MissingRequestedLanguage_FallsBackToDefault` | An `ar` request against an `en`-only product returns the English title, not null. |
| `CatalogueProfileTests.Configuration_IsValid` | `MapperConfiguration.AssertConfigurationIsValid()`. Catches an unmapped `required` member at test time rather than at request time. |

### Integration tests (`MangaStore.IntegrationTests`)

Each test class seeds its own products directly through `AppDbContext` — Phase 04's seeder does not exist yet, and these tests should not depend on it when it does.

| Test | Asserts |
|---|---|
| `CatalogueApiTests.List_ReturnsPaginationEnvelope` | All seven envelope fields present with correct values. |
| `CatalogueApiTests.List_StockStatus_IsCamelCaseString` | Raw JSON contains `"stockStatus":"inStock"`, not `0`. The Phase 01 converter, proved end to end. |
| `CatalogueApiTests.List_Kind_IsDiscriminatorAndFirstProperty` | `"kind":"giftCard"` present; no `$type`. |
| `CatalogueApiTests.List_FilterByType_ExcludesOtherKind` | `?type=giftCard` returns no manga. |
| `CatalogueApiTests.List_InStockOnly_ExcludesOutOfStockButKeepsLowAndPreOrder` | The exact semantics the client's filter relies on. |
| `CatalogueApiTests.List_OnSaleOnly_RequiresCompareAtPriceAbovePrice` | |
| `CatalogueApiTests.List_SearchMatchesArabicTitleWhenAcceptLanguageIsEnglish` | The both-languages rule. |
| `CatalogueApiTests.List_SortNewest_IsStableAcrossPages` | Two products sharing a `ReleasedOn`; assert neither appears on both pages nor on neither. The tiebreak, proved. |
| `CatalogueApiTests.List_InactiveProduct_IsAbsent` | `IsActive = false` never appears. |
| `CatalogueApiTests.List_SoftDeletedProduct_IsAbsent` | |
| `CatalogueApiTests.GetBySlug_ArabicAcceptLanguage_ReturnsArabicTitleAsString` | The payload carries a string, not `{ en, ar }`. |
| `CatalogueApiTests.GetBySlug_Inactive_Returns404` | Not 403. |
| `CatalogueApiTests.GetBySlug_ReleasedOn_IsDateOnlyFormat` | `"releasedOn":"2026-05-14"`, ten characters, no `T`. |
| `CatalogueApiTests.Related_ExcludesSelfAndOtherKinds` | |
| `CatalogueApiTests.Related_TakeIsClamped` | `?take=99999` returns at most 12. |
| `CatalogueApiTests.Resolve_PreservesNothingForUnknownIds` | Partial resolution succeeds. |
| `CatalogueApiTests.Categories_ProductCountExcludesInactive` | A category with one active and one inactive product reports `1`. |
| `CatalogueApiTests.Categories_EmptyCategory_IsListedWithZero` | |
| `CatalogueApiTests.AllReadEndpoints_WorkWithoutAToken` | No `Authorization` header on any of the five; all return 2xx. |

### Edge cases

- `?search=50%` — the `%` is escaped and does not become a wildcard.
- `?search=` (empty) — treated as absent, not as "match empty title".
- `?pageSize=0` and `?pageSize=1000` — clamp to 1 and 100.
- `?pageNumber=-5` — clamps to 1.
- `?categorySlug=does-not-exist` — 200 with an empty page, not 404.
- `Accept-Language: ar` on a product with no Arabic translation — English title, not null or empty.
- A product in zero categories — `categories: []`, still listed.
- A manga whose author has no translation in either language — a seeding defect; assert the query does not throw and the phase's acceptance criteria require the seeder to prevent it.

### Authorization tests

All five endpoints anonymously, and all five with a valid `Customer` bearer token. Both must return the same data. A read endpoint that behaves differently for a signed-in user is a bug waiting to be a privacy leak.

---

## Acceptance Criteria

- [ ] `CatalogController` with five actions, each carrying its own `[AllowAnonymous]`. No class-level `[AllowAnonymous]`.
- [ ] Every action declares `[ProducesResponseType<T>]` for each status it can return, in the generic form; error statuses declare `ProblemDetails`.
- [ ] Every action body is one line — call the service, pass the result to `HandleResult`, return.
- [ ] `ProductSummaryDto` polymorphic on `kind` with `manga` and `giftCard` derived types; no separate `Kind` member on the base.
- [ ] `UseOneOfForPolymorphism()` configured so Swagger and Scalar describe the union.
- [ ] `CatalogueQueryParams` inherits `PaginationParams` and binds `type`, not `kind`.
- [ ] Both request types have FluentValidation validators, invoked as the first line of the corresponding service method.
- [ ] Search matches title, author, brand and numeric denomination, in both languages, regardless of `Accept-Language`.
- [ ] All five sorts implemented, each with a deterministic tiebreak.
- [ ] Page numbers past the end clamp to the last page; the returned `PageNumber` is the effective one.
- [ ] Every public query filters `IsActive = true`; soft-deleted rows are excluded by the query filter.
- [ ] Localized fields resolve through `IRequestLanguage` with a fallback to `en`, and serialise as a single string.
- [ ] `CategoryDto.ProductCount` counts active, non-deleted products; empty categories still appear.
- [ ] Repository queries use `.AsSplitQuery()` and include everything the profile reads.
- [ ] `MapperConfiguration.AssertConfigurationIsValid()` passes.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds with XML docs on every public member.
- [ ] `dotnet test` green.
- [ ] **Manual check against a running API**: `GET /api/v1/catalog/products?type=giftCard&sort=rating&pageSize=12` returns the envelope with `stockStatus` and `kind` as camelCase strings and `releasedOn` as `YYYY-MM-DD`.
- [ ] The localization shape change is written into the PR description as a required frontend follow-up.

---

## Dependencies

```text
Depends on:
  Phase 01 - JsonStringEnumConverter, IRequestLanguage, CommerceOptions.LowStockThreshold,
             PaginatedList/PaginationParams.
  Phase 02 - every catalogue entity, StockStatus.Derive, the translation tables.

Blocks:
  Phase 05 (inventory)  - the admin low-stock views reuse these query methods.
  Phase 06 (cart)       - cart lines resolve products through the same repository.
  Phase 08 (orders)     - order placement reprices through the same repository.
  Phase 13 (admin CRUD) - adds write actions to this controller.
  Phase 14 (covers)     - fills CoverImageUrl, which this DTO already carries.

Can be implemented independently:
  No - requires Phases 01 and 02. Phase 04 (seed) is NOT a dependency:
  these endpoints are testable with data the tests insert themselves, and
  merging this before the seeder is a perfectly good state.
```
