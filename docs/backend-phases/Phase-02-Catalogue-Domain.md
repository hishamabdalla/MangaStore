# Phase 02 — Catalogue Domain and Schema

**Recommended branch:** `phase-02-catalogue-domain`

---

## Objective

Give the shop something to sell. Model the product hierarchy, its supporting dimensions, and its translations; configure them with Fluent API; produce one migration.

**No endpoint ships in this phase.** Splitting the schema from the read API keeps the migration reviewable on its own and lets Phase 03 be about querying rather than about modelling.

---

## Current State

There is **no catalogue of any kind**. `AppDbContext` has exactly one custom `DbSet` (`RefreshTokens`) and the database has eight tables, all of them Identity plus refresh tokens. `CLAUDE.md` closes with "the catalogue domain has not been designed yet", and the `Product*` names in its convention tables are explicitly labelled illustrative.

What Phase 01 left in place and this phase relies on:

- `decimal(18,2)` and `DateOnly → date` conventions on `AppDbContext`.
- `Guid.CreateVersion7()` on `BaseEntity.Id`.
- The filtered-unique-index pattern for slugs.
- `JsonStringEnumConverter` with camelCase, so the enums defined here serialise correctly in Phase 03.
- `IRequestLanguage` and `SupportedLanguages` (`en`, `ar`), which the translation tables are keyed against.
- `CommerceOptions.LowStockThreshold`, used by the `StockStatus` derivation defined here.

The authoritative shape comes from `manga-store\docs\BACKEND-API-GUIDELINE.md` §2 and, where the two disagree, from the TypeScript in `manga-store\src\app\core\catalog\models\`, which is newer.

---

## Scope

Domain entities, their repository interfaces, EF configurations, `DbSet`s and one migration.

| Concern | Entities |
|---|---|
| Product hierarchy | `Product`, `MangaDetail`, `GiftCardDetail` |
| Manga dimensions | `Author`, `Publisher`, `Volume` |
| Gift-card dimensions | `Brand` |
| Shared | `Category`, `ProductCategory` (join) |
| Translations | `ProductTranslation`, `CategoryTranslation`, `AuthorTranslation`, `PublisherTranslation`, `VolumeTranslation`, `GiftCardDetailTranslation`, `GiftCardRedemptionStep` |
| Enums | `ProductKind`, `StockStatus`, `ReadingDirection`, `DeliveryType`, `InventoryMode` |

Also in scope: the `StockStatus` derivation function, and the inventory columns (`InventoryMode`, `IsActive`, `RowVersion`) that Phase 05 will give behaviour to.

### Out of scope

Controllers, DTOs, AutoMapper profiles, validators, services, seeding. Cart, order, coupon and wishlist entities. Cover-image upload.

### Why the inventory columns land here and not in Phase 05

`IsActive` and `InventoryMode` are read by **every** catalogue query — an inactive product must never appear in a public list. If Phase 03 shipped without them, Phase 05 would have to revisit every query it wrote. Defining the columns here costs nothing and means the catalogue is correct from its first request.

Phase 05 owns the **behaviour**: atomic decrement, oversell prevention, admin adjustment, low-stock reporting. Phase 02 owns only the columns and the read-side derivation.

---

## Database Changes

### Enums

Domain, `MangaStore.Domain.Features.Catalogue`. Wire values are produced by Phase 01's camelCase converter, so the C# member names below map exactly to what the client expects.

```csharp
/// <summary>What sort of thing is being sold. Discriminates the per-kind detail row.</summary>
public enum ProductKind { Manga, GiftCard }

/// <summary>Availability, derived from stock and release date. Never persisted.</summary>
public enum StockStatus { InStock, LowStock, PreOrder, OutOfStock }

/// <summary>Whether the shop counts units of this product.</summary>
public enum InventoryMode { Tracked, Unlimited }

/// <summary>Reading order of the printed edition.</summary>
public enum ReadingDirection { RightToLeft, LeftToRight }

/// <summary>How a redeemed gift-card code reaches the buyer.</summary>
public enum DeliveryType { InstantEmail, AccountCredit }
```

### `Product` — the shared row

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | From `BaseEntity`, v7 |
| `Slug` | `nvarchar(160)` | Unique, filtered on `IsDeleted = 0`. The public identifier; URLs use it, never the id |
| `Kind` | `int` | `ProductKind` discriminator |
| `Price` | `decimal(18,2)` | What the shop charges |
| `CompareAtPrice` | `decimal(18,2)` NULL | Set only while discounted; **must exceed `Price`** |
| `Currency` | `nchar(3)` | ISO 4217 for the price |
| `InventoryMode` | `int` | `Tracked` or `Unlimited` |
| `StockQuantity` | `int` | Meaningful only when `Tracked`. Never negative |
| `IsActive` | `bit` | Sellable. Separate from stock — see Business Rules |
| `ReleasedOn` | `date` | Drives `newest` sort, the client's 90-day "new" badge, and `PreOrder` |
| `AverageRating` | `decimal(2,1)` | Denormalised. Phase 15 makes it real |
| `RatingCount` | `int` | Denormalised |
| `CoverImagePath` | `nvarchar(400)` NULL | Relative path; Phase 14 fills it |
| `RowVersion` | `rowversion` | Optimistic concurrency token. Phase 05 uses it |
| `CreatedAt` / `UpdatedAt` / `IsDeleted` | | From `BaseEntity` |

Indexes: unique filtered on `Slug`; non-unique on `(Kind, IsActive, IsDeleted)` for the type tabs; non-unique on `ReleasedOn DESC` for the default sort; non-unique on `Price` for the price sorts and range filter.

> `RowVersion` is a `byte[]` mapped with `.IsRowVersion()`. **SQLite does not support it** — the integration test provider will ignore the concurrency check. Phase 05 and Phase 16 say what to do about that; do not let a green SQLite test suite convince you concurrency is covered.

### `MangaDetail` — one-to-one with `Product` where `Kind = Manga`

| Column | Type | Notes |
|---|---|---|
| `ProductId` | `uniqueidentifier` | FK, unique, cascade delete |
| `Isbn` | `nvarchar(20)` | Unique, filtered on `IsDeleted = 0` |
| `ReadingDirection` | `int` | |
| `AgeRating` | `nvarchar(16)` | Free text: `16+`, `13+`, `18+`, `All ages` |
| `AuthorId` | `uniqueidentifier` | FK, required, restrict delete |
| `PublisherId` | `uniqueidentifier` | FK, required, restrict delete |

`VolumeCount` is **not** a column — the client's `MangaSummary.volumeCount` is `Volumes.Count`, computed in the projection. Storing it would be a denormalisation nobody asked for and a second thing to keep in step.

### `GiftCardDetail` — one-to-one with `Product` where `Kind = GiftCard`

| Column | Type | Notes |
|---|---|---|
| `ProductId` | `uniqueidentifier` | FK, unique, cascade delete |
| `BrandId` | `uniqueidentifier` | FK, required, restrict delete |
| `DenominationAmount` | `decimal(18,2)` | **Face value printed on the card** |
| `DenominationCurrency` | `nchar(3)` | **Not** the price currency — see Business Rules |
| `DeliveryType` | `int` | |

### `Volume` — owned by a manga

| Column | Type | Notes |
|---|---|---|
| `MangaDetailId` | `uniqueidentifier` | FK, cascade delete |
| `Number` | `int` | Unique with `MangaDetailId` |
| `PageCount` | `int` | |
| `ReleasedOn` | `date` | |

### Dimensions

| Entity | Columns |
|---|---|
| `Category` | `Slug` (unique, filtered), `Kind` (`ProductKind`) |
| `Author` | — (translated name only) |
| `Publisher` | — (translated name only) |
| `Brand` | `Key` (unique, e.g. `steam`), `Name` `nvarchar(80)` **not translated**, `AccentToken` `nvarchar(64)` |

`Brand.Name` is a plain string because the TypeScript types it as `string`, not `LocalizedText` — platform names are not translated. `AccentToken` holds a MangaStore ramp step such as `var(--ms-blue-600)`; the seed's comment is explicit that these are the shop's own colours and imply no partnership, and nothing in the data should suggest otherwise.

### `ProductCategory` — many-to-many

Join table on `(ProductId, CategoryId)`, both FKs cascading. Configure as a skip navigation via `UsingEntity` so `Product.Categories` and `Category.Products` both work.

> The guideline says categories are many-to-many with `Manga`. That is stale: `ProductBase.categories` is on **every** product in the TypeScript, and gift cards carry the `steam` / `playstation` / `xbox` / `nintendo` categories. Join to `Product`.

### Translations

One table per translated entity, each keyed `(OwnerId, LanguageCode)`. `LanguageCode` is `nvarchar(5)`, values from `SupportedLanguages.All`.

| Table | Translated columns |
|---|---|
| `ProductTranslations` | `Title` `nvarchar(200)`, `Description` `nvarchar(max)` NULL |
| `CategoryTranslations` | `Name` `nvarchar(120)` |
| `AuthorTranslations` | `Name` `nvarchar(160)`, `Biography` `nvarchar(max)` NULL |
| `PublisherTranslations` | `Name` `nvarchar(160)` |
| `VolumeTranslations` | `Title` `nvarchar(200)` |
| `GiftCardDetailTranslations` | `Region` `nvarchar(120)`, `Terms` `nvarchar(max)` |
| `GiftCardRedemptionSteps` | `Ordinal` `int`, `Text` `nvarchar(500)` — PK `(GiftCardDetailId, LanguageCode, Ordinal)` |

`ProductTranslations.Description` serves both kinds: it surfaces as `synopsis` on a manga DTO and as `description` on a gift-card DTO. One column, because it is the same thing — the long-form prose about the product — and two would mean every query picking which to read based on `Kind`.

Index `ProductTranslations (LanguageCode, Title)` and `AuthorTranslations (LanguageCode, Name)`; the search filter hits both, in both languages.

### Rejected: a single polymorphic translation table

`Translations(EntityType, EntityId, LanguageCode, Field, Value)` would collapse seven tables into one. It is the EAV anti-pattern: no type safety, no useful index for "title in Arabic", no foreign key, and a query planner that cannot help. Seven narrow tables with real keys are cheaper to query and impossible to misuse.

### Rejected: EF Core TPT/TPH inheritance for the product hierarchy

Modelling `Manga : Product` and `GiftCard : Product` as a CLR hierarchy is the obvious alternative. It is rejected for one concrete reason: **EF Core forbids `HasQueryFilter` on a derived type** — the filter must be declared on the root of the hierarchy. Since `HasQueryFilter(e => !e.IsDeleted)` is required on every entity, inheritance would force a documented exception to the rule and a soft-delete story that differs from every other entity in the codebase.

The one-to-one detail tables give the same normalised shape, keep every entity's soft-delete identical, and map more directly onto the client's discriminated union.

The cost is that `Kind = Manga` with no `MangaDetail` row is representable in the database. That is handled in Business Rules.

### Migration

```bash
dotnet ef migrations add AddCatalogue \
  --project src/MangaStore.Infrastructure \
  --startup-project src/MangaStore.API
```

Sixteen new tables. Review the generated SQL before committing — in particular that the filtered unique indexes carry `WHERE [IsDeleted] = 0`, that `RowVersion` is a `rowversion` and not a `varbinary`, and that no cascade path from `Product` reaches a dimension table.

---

## API Contract

**None.** This phase adds no controller and no endpoint. Phase 03 is the first thing a client can call.

---

## Business Rules

### `StockStatus` is derived, never stored

The client renders `{{ 'stock.' + stockStatus | translate }}` and filters on `inStockOnly`. The value is computed on read:

```csharp
/// <summary>Availability of a product, derived from its release date and stock.</summary>
public static StockStatus Derive(
    DateOnly releasedOn, DateOnly today, InventoryMode mode, int stockQuantity, int lowStockThreshold)
{
    if (releasedOn > today)
    {
        return StockStatus.PreOrder;
    }

    if (mode == InventoryMode.Unlimited)
    {
        return StockStatus.InStock;
    }

    return stockQuantity switch
    {
        <= 0 => StockStatus.OutOfStock,
        _ when stockQuantity <= lowStockThreshold => StockStatus.LowStock,
        _ => StockStatus.InStock,
    };
}
```

> **The pre-order check comes first, and this deliberately diverges from the guideline.** §2 lists the rules as "`0` → `OutOfStock`, below the low-stock threshold → `LowStock`, a future `ReleasedOn` → `PreOrder`, otherwise `InStock`". Applied in that order, every pre-order product reports `outOfStock`, because an unreleased product has no stock yet — which is precisely the case the `PreOrder` status exists to describe. Three of the thirty seeded manga are pre-orders and would all render wrongly. Release date is checked first.

`InventoryMode.Unlimited` answers the brief's requirement that a product which does not need quantity-based inventory is not forced into the same behaviour. Digital products the shop can supply on demand use it; gift cards backed by a finite pool of codes use `Tracked`.

### `IsActive` and stock are separate concepts

`IsActive = false` with `StockQuantity = 20` means the product has inventory but is not for sale. Withdrawing a product from sale and running out of it are different events with different fixes, and collapsing them loses that.

| Flag | Meaning | Public catalogue | Admin catalogue |
|---|---|---|---|
| `IsActive = true`, stock > 0 | On sale | Visible, buyable | Visible |
| `IsActive = true`, stock = 0 | Sold out | Visible, `outOfStock`, not buyable | Visible |
| `IsActive = false` | Withdrawn | **Absent entirely** | Visible |
| `IsDeleted = true` | Removed | Absent | Absent unless `IgnoreQueryFilters` |

The frontend has no concept of an inactive product — `isActive` appears nowhere in `manga-store\src`. So an inactive product must be *absent* from public responses rather than flagged in them. Phase 03 applies `IsActive` as a filter on every public query; Phase 13 exposes it to admins.

### Face value is not the selling price

`GiftCardDetail.DenominationAmount` + `DenominationCurrency` are the value printed on the card. `Product.Price` + `Currency` are what the shop charges. They are different numbers, possibly in different currencies, and **the selling price must never be written into the face value**.

A `$70` Steam card is a `$70` card wherever it is sold. The seeded example charges `71.49 USD` for it; a shop pricing in EGP might charge `4025 EGP` for the same `70 USD` card. Both are correct, and neither changes the denomination.

The client models this as a nested object and formats the two independently:

```ts
readonly denomination: Denomination;  // { amount: 70, currency: 'USD' }  ← face value
readonly price: number;               // 71.49                            ← selling price
readonly currency: string;            // 'USD'
```

### `CompareAtPrice` must exceed `Price`

`discountPercent()` on the client returns `null` when `compareAtPrice <= price`, so a bad value renders as "no discount" rather than "0% off" or a negative saving. The backend should not rely on that: enforce `CompareAtPrice IS NULL OR CompareAtPrice > Price` as a check constraint, and again in the Phase 13 validator.

### Kind and detail row must agree

The database cannot cheaply express "a `Kind = Manga` product has exactly one `MangaDetail` and no `GiftCardDetail`" — that is a cross-table invariant, and a trigger to enforce it would be a maintenance burden for a rule the application already owns.

Enforce it in the domain, following `docs/entity-factory-vs-mapper.md`: properties have `private set`, and construction goes through static factories that cannot produce a mismatched pair.

```csharp
public sealed class Product : BaseEntity
{
    private readonly List<ProductTranslation> _translations = [];
    private readonly List<Category> _categories = [];

    private Product() { }

    /// <summary>Creates a manga product together with its detail row.</summary>
    public static Product CreateManga(string slug, decimal price, string currency, /* ... */) { /* sets Kind = Manga and Manga = new MangaDetail(...) */ }

    /// <summary>Creates a gift-card product together with its detail row.</summary>
    public static Product CreateGiftCard(string slug, decimal price, string currency, /* ... */) { /* sets Kind = GiftCard and GiftCard = new GiftCardDetail(...) */ }

    /// <summary>Gets the manga-specific detail, or null when this is not a manga.</summary>
    public MangaDetail? Manga { get; private set; }

    /// <summary>Gets the gift-card-specific detail, or null when this is not a gift card.</summary>
    public GiftCardDetail? GiftCard { get; private set; }
}
```

Both navigations are optional at the EF level and exactly one is populated by construction. Phase 03's projection narrows on `Kind`; if it ever finds `Kind = Manga` with a null `Manga`, that is a data-integrity bug and should fail loudly rather than return a half-filled DTO.

### Translation fallback

A row missing a translation in the requested language falls back to `SupportedLanguages.Default` (`en`). A row missing **both** is a seeding defect; return the default-language row if present, and if not, fail rather than emit an empty title.

Fallback is resolved in the query, not by a second round trip:

```csharp
.Select(p => new
{
    Title = p.Translations.FirstOrDefault(t => t.LanguageCode == language)!.Title
         ?? p.Translations.First(t => t.LanguageCode == SupportedLanguages.Default).Title,
})
```

### Soft delete and slugs

`Product.Slug`, `Category.Slug` and `MangaDetail.Isbn` are unique. Without a filter, a soft-deleted product holds its slug forever and re-creating it fails with a 409 the admin cannot explain. Every one of these indexes carries `.HasFilter("[IsDeleted] = 0")`, per Phase 01.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | None — no endpoint exists yet. |
| Authorization | None yet. Phase 03 makes reads anonymous; Phase 13 restricts writes to `Roles.Admin`. |
| Validation | No request DTOs yet. Constraints are expressed in the schema: check constraints on `CompareAtPrice` and on `StockQuantity >= 0`, `NOT NULL` where a value is required, and length limits on every string. |
| Sensitive data | None. Nothing in the catalogue schema is confidential. **Gift-card codes are not modelled here** and must not be — Phase 09 owns that, in a separate table, encrypted, and never joined into a public projection. |
| Concurrency | `RowVersion` is added but not yet used. Phase 05 turns it on. |
| Rate limiting | Not applicable. |

> The one thing to get right for security in this phase is a negative: resist the temptation to add a `Code` or `Codes` column to `GiftCardDetail` "for later". A sellable code that lives on the product row will end up in a public projection the first time someone writes `.Include(p => p.GiftCard)`.

---

## Frontend Contract

Nothing is consumed yet — no endpoint exists. What this phase fixes is the *shape* Phase 03 will return, so it is worth checking each entity against its TypeScript counterpart now rather than after the DTOs are written.

| Backend | TypeScript | File |
|---|---|---|
| `Product` | `ProductBase` | `models/product.model.ts` |
| `Product.Kind` | `ProductKind = 'manga' \| 'giftCard'` | same |
| `MangaDetail` + `Product` | `MangaSummary` / `MangaDetail` | `models/manga.model.ts` |
| `GiftCardDetail` + `Product` | `GiftCardSummary` / `GiftCardDetail` | `models/gift-card.model.ts` |
| `GiftCardDetail.DenominationAmount/Currency` | `Denomination { amount, currency }` | same |
| `Brand` | `Brand { id, name, accentToken }` | same |
| `Category` | `Category { id, slug, name, kind, productCount }` | `models/product.model.ts` |
| `Author` | `Author { id, name }` | `models/manga.model.ts` |
| `Volume` | `Volume { number, title, pageCount, releasedOn }` | same |

Two mismatches to carry forward into Phase 03 rather than solve here:

1. **`Category.productCount` is not a column.** It is a computed count of non-deleted, active products in that category. Phase 03 projects it.
2. **Localized fields will change shape on the wire.** The TypeScript types `title`, `name`, `synopsis`, `region`, `publisher`, `terms` as `LocalizedText` (`{ en, ar }`), because the in-memory services hold both languages and call `localize()` in the components. The API returns **one resolved string**. Phase 03 states this in full and names it as a required frontend change.

---

## Testing

No service and no endpoint exists, so testing here is about the model and the migration.

### Unit tests (`MangaStore.UnitTests`)

| Test | Asserts |
|---|---|
| `StockStatusTests.FutureRelease_IsPreOrder_EvenWithZeroStock` | The divergence from the guideline. `ReleasedOn` tomorrow, `StockQuantity` 0 → `PreOrder`, not `OutOfStock`. |
| `StockStatusTests.ZeroStock_IsOutOfStock` | Released, `Tracked`, 0 → `OutOfStock`. |
| `StockStatusTests.AtThreshold_IsLowStock` | `stockQuantity == lowStockThreshold` → `LowStock` (inclusive boundary). |
| `StockStatusTests.AboveThreshold_IsInStock` | `threshold + 1` → `InStock`. |
| `StockStatusTests.Unlimited_IgnoresQuantity` | `Unlimited` with 0 → `InStock`. |
| `ProductTests.CreateManga_PopulatesMangaDetailAndLeavesGiftCardNull` | The factory cannot produce a mismatched pair. |
| `ProductTests.CreateGiftCard_PopulatesGiftCardDetailAndLeavesMangaNull` | The mirror case. |
| `ProductTests.CreateGiftCard_KeepsDenominationSeparateFromPrice` | Face value 70 USD with price 4025 EGP round-trips as two distinct values. |

### Integration tests (`MangaStore.IntegrationTests`)

`CustomWebApplicationFactory` calls `Database.EnsureCreated()`, which builds the schema from the model rather than from migrations — so these tests prove the *model* is coherent, not that the migration is.

| Test | Asserts |
|---|---|
| `CatalogueSchemaTests.SoftDeletedProduct_DoesNotBlockSlugReuse` | Insert, soft-delete, insert again with the same slug. Fails without the filtered index. |
| `CatalogueSchemaTests.CompareAtPriceBelowPrice_IsRejected` | The check constraint bites. |
| `CatalogueSchemaTests.NegativeStock_IsRejected` | The `StockQuantity >= 0` constraint bites. |
| `CatalogueSchemaTests.DuplicateVolumeNumber_IsRejected` | Unique on `(MangaDetailId, Number)`. |
| `CatalogueSchemaTests.QueryFilter_HidesSoftDeletedProduct` | A soft-deleted product is invisible to a plain query and visible under `IgnoreQueryFilters`. |
| `CatalogueSchemaTests.TranslationsRoundTripBothLanguages` | `en` and `ar` rows for one product both persist and read back. |

### Edge cases

- A product with `Kind = Manga` and no `MangaDetail` row: representable in the database, impossible through the factory. Assert the factory path; do not add a runtime guard that will never fire in application code.
- A category with zero products: `productCount` is `0`, not `null`, and the category still appears.
- `Volume.ReleasedOn` in the future while the parent product is already released: allowed, and no `StockStatus` consequence — the derivation reads `Product.ReleasedOn` only.
- `CompareAtPrice` exactly equal to `Price`: rejected by the constraint. The client would treat it as no discount, but storing it is still a data error.

### Verification beyond tests

Run the migration against a real SQL Server, not just SQLite:

```bash
dotnet ef database update --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API
```

The provider differences that matter — `rowversion`, filtered indexes, `decimal(18,2)` precision — are exactly the things SQLite will not exercise.

---

## Acceptance Criteria

- [ ] Five enums defined in Domain with the member names listed above.
- [ ] `Product`, `MangaDetail`, `GiftCardDetail`, `Volume`, `Category`, `Author`, `Publisher`, `Brand` all inherit `BaseEntity`, all have `private set` properties, and all are constructed through static factories.
- [ ] Seven translation tables defined and keyed `(OwnerId, LanguageCode)`.
- [ ] `ProductCategory` configured as a skip navigation joining `Product` and `Category`.
- [ ] One `IEntityTypeConfiguration<T>` per entity, Fluent API only, **no data annotations on any Domain entity**.
- [ ] `HasQueryFilter(e => !e.IsDeleted)` on every entity in this phase.
- [ ] Filtered unique indexes on `Product.Slug`, `Category.Slug`, `MangaDetail.Isbn`, `Brand.Key`.
- [ ] Check constraints: `CompareAtPrice IS NULL OR CompareAtPrice > Price`; `StockQuantity >= 0`.
- [ ] `Product.RowVersion` mapped with `.IsRowVersion()`.
- [ ] `IProductRepository`, `ICategoryRepository`, `IAuthorRepository`, `IBrandRepository` declared in Domain, each extending `IRepository<T>`. Method bodies for search land in Phase 03; declare only what this phase needs.
- [ ] Corresponding repositories in Infrastructure, discovered by Scrutor — **no manual DI registration**.
- [ ] `DbSet` for every root added to `AppDbContext`.
- [ ] `StockStatus.Derive` implemented in Domain with the release-date check first.
- [ ] One migration, `AddCatalogue`, generating 16 tables; reviewed for filtered indexes, `rowversion`, and cascade paths.
- [ ] `dotnet ef migrations has-pending-model-changes` reports no drift.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds — every public member has an XML `<summary>`.
- [ ] `dotnet test` green, including the new schema tests.
- [ ] No controller, DTO, validator, profile, service or seed data was added.

---

## Dependencies

```text
Depends on:
  Phase 01 - decimal/DateOnly conventions, Guid v7, the filtered-index pattern,
             SupportedLanguages, CommerceOptions.LowStockThreshold.

Blocks:
  Phase 03 (catalogue read API) - hard block, nothing to query without it.
  Phase 04 (seed data)          - hard block.
  Phase 05 (inventory)          - hard block, owns the columns defined here.
  Phase 06 (cart), Phase 07 (coupons), Phase 08 (orders) - all reference Product.
  Phase 09, 10, 12, 13, 14, 15  - all reference Product or Category.

Can be implemented independently:
  No - requires Phase 01. But it needs nothing else, and no later phase
  needs to revisit this schema except Phase 09 (gift-card codes),
  Phase 14 (cover variants) and Phase 15 (reviews), each of which adds
  its own tables rather than altering these.
```
