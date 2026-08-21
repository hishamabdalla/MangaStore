# Phase 04 — Catalogue Seed Data

**Recommended branch:** `phase-04-seed-data`

---

## Objective

Make a fresh environment look like a real shop. Seed the categories, authors, publisher, brands, manga and gift cards that the storefront was designed against, in both languages, with a stock spread that exercises every rendering path.

Without this, every catalogue endpoint returns an empty page and nobody can tell a working API from a broken one.

---

## Current State

`IdentitySeeder` exists and is the pattern to follow: resolved from an async scope in `WebApplicationExtensions.SeedIdentityAsync`, called from `Program.cs` **before** the pipeline is configured, and idempotent — it creates each role only if missing and the bootstrap admin only if absent.

The catalogue has no seeder and no data. `manga-store\src\app\core\catalog\in-memory\` holds the complete sample set as TypeScript:

| File | Contents |
|---|---|
| `catalog.seed.ts` | 8 manga categories, 10 authors, 30 manga (577 lines) |
| `gift-card.seed.ts` | 4 gift-card categories, 4 brands, 3 regions, 14 gift cards (220 lines) |
| `coupon.seed.ts` | 3 coupons — **not this phase**, see below |
| `product-images.ts` | Unsplash photo ids in two pools, picked by `hash(slug)` |

Everything in those files is invented: no real series, publisher or creator is named, and no real work has a made-up price attached. **Keep it that way.** Transcribing is the job; inventing new titles that happen to be real ones is not.

---

## Scope

One idempotent `CatalogueSeeder` in Infrastructure, wired into the existing start-up seeding path, plus the data itself.

| Seeded | Count | Source |
|---|---|---|
| Categories | 12 (8 manga + 4 gift-card) | `CATEGORY_SEED`, `GIFT_CARD_CATEGORY_SEED` |
| Authors | 10 | `AUTHOR_SEED` |
| Publishers | 1 | Derived — all 30 manga share one |
| Brands | 4 | `BRAND_SEED` |
| Manga products | 30 | `MANGA_SEED` |
| Gift-card products | 14 | `GIFT_CARD_SEED` |
| Volumes | ~270 | Generated, as the in-memory service does |
| Translations | all of the above, `en` + `ar` | Both languages are in the seed files |

### Out of scope

**Coupons.** `Coupon` does not exist until Phase 07, which extends this seeder with its own codes — including the expired and below-minimum ones the guideline asks for, so the rejection paths can be seen working rather than assumed.

Cover images. `CoverImagePath` is left null; the storefront draws generated artwork when it is absent. Phase 14 revisits this.

---

## Database Changes

**None.** This phase writes rows, not schema.

---

## API Contract

**None.** Seeding runs at start-up, not through an endpoint.

> Do **not** add a `POST /admin/seed` endpoint. A seeding endpoint on a running system is a data-loss button with a friendly name, and there is no requirement for one.

---

## Business Rules

### Idempotency

Follow `IdentitySeeder`: check, then create. Match on the natural key — `Slug` for products and categories, `Key` for brands, a stable seed key for authors and publishers.

```csharp
if (await _context.Products.AnyAsync(p => p.Slug == seed.Slug, ct))
{
    continue;
}
```

**Insert only; never update.** A seeder that overwrites will undo an admin's price change on the next deploy. If a seeded value needs to change, that is a migration or an admin edit, not a seeder concern.

Seeding must be safe to run on every start-up, because it is. Run the whole thing inside one `SaveChangesAsync` so a partial catalogue is never committed.

### Deterministic ids

Do not let `Guid.CreateVersion7()` allocate seed ids. Derive them deterministically from the natural key, so the same product has the same id in every environment:

```csharp
/// <summary>Derives a stable id from a seed key, so seeded rows match across environments.</summary>
private static Guid SeedId(string key) =>
    new(MD5.HashData(Encoding.UTF8.GetBytes($"mangastore:{key}")));
```

This matters more than it looks. Phase 07 seeds an item-scoped coupon pointing at `steam-gift-card-70`; without stable ids that reference has to be resolved by slug at seed time, and any cross-environment fixture — a Postman collection, a frontend test, a support ticket quoting a product id — breaks on every rebuild.

> MD5 here is a name-to-id derivation, not a security primitive. It is not hashing a secret, and the value it produces is a public identifier. Note that in a comment so a future scan does not flag it as a weak-hash finding.

### Stock states must be derived, not copied

The frontend seeds `stockStatus` directly. The backend **derives** it from `ReleasedOn`, `InventoryMode` and `StockQuantity` (Phase 02). So the seeder translates in the other direction:

| Frontend `stockStatus` | Seeded as |
|---|---|
| `inStock` | `Tracked`, `StockQuantity` 25–80, `ReleasedOn` in the past |
| `lowStock` | `Tracked`, `StockQuantity` 1–`LowStockThreshold`, `ReleasedOn` in the past |
| `outOfStock` | `Tracked`, `StockQuantity` 0, `ReleasedOn` in the past |
| `preOrder` | `Tracked`, `StockQuantity` 0, **`ReleasedOn` in the future** |

The guideline asks for "a mix of stock states including at least one out-of-stock and one pre-order — the frontend renders those differently and they need to be visible." The sample set already has that spread: one out-of-stock (`midnight-cram-school`), three pre-orders (`ember-circuit`, `signal-from-ward-nine`, `rust-angel`), three low-stock (`paper-lanterns-at-dawn`, `seventh-summer`, `the-understudy-swordsman`), and nine carrying a `compareAtPrice`.

### Dates must be relative, not absolute

**This is the part that will silently rot.** The frontend seed carries fixed dates such as `releasedOn: '2026-05-14'`. Copy them literally and two things break as time passes:

- Every **pre-order** becomes a past release, so it renders as out-of-stock instead of pre-order and the three seeded pre-orders disappear as a testable state.
- Every **new arrival** ages past the client's 90-day window (`NEW_ARRIVAL_DAYS`), so the "New" badge stops appearing and the `newest` rail on the home page looks arbitrary.

Anchor the dates to `IDateTime.UtcNow` instead, preserving the sample's *relative* spread:

```csharp
var today = DateOnly.FromDateTime(_dateTime.UtcNow);

// Pre-orders release in the future, so the status stays reachable however
// long after this was written the seeder runs.
DateOnly ReleaseFor(MangaSeed s) => s.StockState switch
{
    SeedStockState.PreOrder => today.AddDays(30 + s.Offset),
    _ => today.AddDays(-s.DaysSinceRelease),
};
```

Keep at least six products inside the 90-day window so the "New" badge and the new-arrivals rail have something to show, and spread the rest across roughly the previous eighteen months, matching the sample's 2025-08 → 2026-08 range.

### Derived manga fields

The in-memory service computes these rather than seeding them; the seeder must do the same:

| Field | Rule |
|---|---|
| `Isbn` | Generated from a hash of the slug as `978-D-DDD-DDDDD-0`. Must be unique across all 30 |
| `ReadingDirection` | `RightToLeft` for all 30 |
| `Publisher` | One publisher: `MangaStore Press` / `دار مانجا ستور` |
| `Currency` | `USD` for every product |
| `Volumes` | `volumeCount` per title; `PageCount = 176 + ((hash % 6) + 1) * 8`; release dates spaced 90 days back from the product's release |
| `Volume` titles | Generated per volume, in both languages |

### Gift-card fields

`slug` is `{brandKey}-gift-card-{denomination}` — `steam-gift-card-70`. Title is `{Brand.Name} Gift Card ${denomination}`. `DenominationCurrency` is `USD` for all fourteen.

**The selling price always exceeds the face value**, because the shop takes a margin — a `$70` card sells for `$71.49`, a `$5` card for `$5.49`. Preserve that relationship exactly; it is the clearest demonstration in the whole dataset that `Price` and `DenominationAmount` are different numbers.

`description`, `redemptionSteps` (three, ordered) and `terms` are generated per card in both languages, as `in-memory-catalog.service.ts` does.

Gift cards use `InventoryMode.Tracked` — Phase 09 backs them with a finite pool of codes, and a card that can be sold without a code to deliver is worse than one that sells out.

### Currency and the EGP question

Every seeded price is in `USD`, matching the sample data. `Currency` is per-product, so a shop pricing in EGP is fully supported by the schema — a `70 USD` face value with a `4025 EGP` selling price is exactly the case Phase 02's separation exists for.

**Do not silently switch the seed to EGP.** The frontend's sample data, its tests and its formatting expectations are all USD-based. Changing the trading currency is a business decision, and if it is wanted it should be a deliberate follow-up that changes the seed and the frontend's expectations together.

### Where the seeder runs

Extend the existing path rather than adding a parallel one:

```csharp
// WebApplicationExtensions
await app.SeedIdentityAsync();
await app.SeedCatalogueAsync();
```

Same async scope pattern, same idempotency, same "do not migrate here" rule — `WebApplicationExtensions` deliberately leaves `dotnet ef database update` an explicit step, and this phase does not change that.

Guard it with configuration so a production deployment can decline:

```json
"Catalogue": { "SeedSampleData": true }
```

Default `true` in Development, `false` in Production. Sample data in a real shop is worse than no data.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | None — no endpoint. |
| Authorization | None. |
| Validation | The seeder bypasses `IValidationService` because it constructs entities directly through the Phase 02 factories, which enforce the invariants. Seeded data must still satisfy every check constraint. |
| Sensitive data | **No gift-card codes are seeded.** Phase 09 owns that table, and seeding sellable codes into a shared development database is how they leak. Seed the products; leave the code pool empty. |
| Concurrency | Two instances starting simultaneously could both pass the `AnyAsync` check and both insert. The unique index on `Slug` turns the loser into a `DbUpdateException` rather than a duplicate. Catch it, log it, and continue — the desired state was reached either way. |
| Rate limiting | Not applicable. |

---

## Frontend Contract

Nothing new is consumed — the endpoints exist from Phase 03. What changes is that they return data.

After this phase, with `HttpCatalogService` bound, the storefront should be indistinguishable from its in-memory version except for the localization shape change noted in Phase 03. Specifically these must all still work:

| Surface | Requires |
|---|---|
| Home spotlight rail | `?sort=rating&pageSize=5` returns 5 |
| New arrivals rail | ≥ 6 products within `NEW_ARRIVAL_DAYS` (90) of today |
| Gift-card rail | `?type=giftCard` returns 14 |
| On-sale rail and deal of the day | ≥ 9 products with `CompareAtPrice` above `Price` |
| Catalogue type tabs | Both `manga` and `giftCard` non-empty |
| Genre tiles | All 12 categories with a non-zero `productCount` where the sample has products |
| Stock badge paths | At least one each of `inStock`, `lowStock`, `preOrder`, `outOfStock` visible |
| Arabic mode | Every seeded product has an `ar` translation |

---

## Testing

### Unit tests

The seeder is mostly data, and asserting the contents of a data file against itself is not a test. Test the parts with logic:

| Test | Asserts |
|---|---|
| `SeedIdTests.SameKey_ProducesSameGuid` | Determinism, twice in the same process and across a serialised round trip. |
| `SeedIdTests.DifferentKeys_ProduceDifferentGuids` | No collisions across all 44 product slugs plus categories, authors and brands. |
| `SeedDateTests.PreOrderRelease_IsInTheFuture` | Given any "today", pre-orders release after it. The guard against the date-rot failure. |
| `SeedDateTests.NewArrivalCount_IsAtLeastSix` | At least six products fall within 90 days of "today". |
| `SeedIsbnTests.AllGenerated_AreUnique` | 30 slugs produce 30 distinct ISBNs. |

### Integration tests

| Test | Asserts |
|---|---|
| `CatalogueSeederTests.RunTwice_ProducesOneCatalogue` | Call `SeedAsync` twice; product count stays 44. **The core idempotency test.** |
| `CatalogueSeederTests.SeedsExpectedCounts` | 12 categories, 10 authors, 1 publisher, 4 brands, 30 manga, 14 gift cards. |
| `CatalogueSeederTests.EveryProductHasBothTranslations` | No product has fewer than two `ProductTranslation` rows, and neither title is empty. |
| `CatalogueSeederTests.EveryAuthorAndCategoryHasBothTranslations` | Same, for the dimensions. |
| `CatalogueSeederTests.StockSpreadCoversEveryStatus` | Deriving `StockStatus` across all 44 yields at least one of each of the four values. |
| `CatalogueSeederTests.GiftCardPriceExceedsDenomination` | All 14. The face-value separation, demonstrated by the data. |
| `CatalogueSeederTests.NineProductsAreOnSale` | `CompareAtPrice > Price` count matches the sample. |
| `CatalogueSeederTests.SeededIdsAreStableAcrossRuns` | Tear down, re-seed, ids unchanged. |
| `CatalogueSeederTests.DisabledByConfiguration_SeedsNothing` | `Catalogue:SeedSampleData=false` → zero products. |

### Edge cases

- Seeder runs against a database that already has an **admin-created** product with a colliding slug: the `AnyAsync` check skips it and the admin's row wins. Correct — never overwrite.
- Seeder runs concurrently from two instances: one gets a unique-index violation, catches it, logs, continues.
- `Catalogue:SeedSampleData` absent: default per environment, not a start-up failure.
- A seed record missing its Arabic translation: should be impossible, and `EveryProductHasBothTranslations` is the test that keeps it that way.

---

## Acceptance Criteria

- [ ] `CatalogueSeeder` in Infrastructure, registered scoped, following `IdentitySeeder`'s shape.
- [ ] `SeedCatalogueAsync()` extension on `WebApplication`, called from `Program.cs` after `SeedIdentityAsync()`.
- [ ] Gated by `Catalogue:SeedSampleData`, defaulting to `true` in Development and `false` in Production.
- [ ] Insert-only and idempotent: running twice leaves 44 products.
- [ ] Deterministic ids derived from natural keys, stable across environments and rebuilds.
- [ ] 12 categories, 10 authors, 1 publisher, 4 brands, 30 manga, 14 gift cards, all with `en` and `ar` translations.
- [ ] Release dates anchored to "today", not hard-coded: pre-orders in the future, at least six products inside the 90-day new-arrival window.
- [ ] Deriving `StockStatus` over the seeded set produces all four values.
- [ ] All 14 gift cards price above their denomination; `DenominationCurrency` is set independently of `Currency`.
- [ ] Volumes generated with unique `(MangaDetailId, Number)` and plausible page counts.
- [ ] ISBNs unique across all 30 manga.
- [ ] No gift-card codes seeded. No coupons seeded — Phase 07 adds those.
- [ ] `CoverImagePath` left null throughout.
- [ ] Nothing invented beyond the sample set: no real series, creator or publisher named.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.
- [ ] **Manual check**: start the API against an empty database, then `GET /api/v1/catalog/products?pageSize=100` returns 44 items, and `GET /api/v1/catalog/categories` returns 12 with non-zero counts.

---

## Dependencies

```text
Depends on:
  Phase 02 - every catalogue entity and its factories.
  Phase 01 - IDateTime for the relative dates, CommerceOptions.LowStockThreshold
             for the low-stock quantities.

Blocks:
  Nothing hard. Every later phase can be built and tested without it.

Can be implemented independently:
  Yes, once Phase 02 is merged. It does NOT depend on Phase 03 - the seeder
  writes through the DbContext, not through the API. Doing it after Phase 03
  is only more satisfying, because the data becomes immediately visible.

  Later phases extend this seeder rather than replacing it:
    Phase 07 - coupons, including one expired and one below-minimum.
    Phase 09 - deliberately seeds NO gift-card codes.
```
