# MangaStore Backend — Phase Index

Sixteen phases that take the repository from an auth-only Clean Architecture template to a working multi-product storefront API.

Each phase file is self-contained and can be handed to Claude Code on its own. Read this index first: it carries the decisions that apply across all of them, the order they go in, and the branch strategy.

---

## Where things stand

| | |
|---|---|
| **Backend** | `E:\store\MangaStore` — .NET 10, Clean Architecture, plain service layer. No MediatR, no CQRS, no Minimal APIs |
| **Ships today** | Auth and self-profile. 2 controllers, 10 endpoints, 1 domain entity (`RefreshToken`), 1 migration, 8 tables |
| **Frontend** | `E:\store\manga-store` — Angular 21.2, zoneless, signals, standalone, PrimeNG 21, Tailwind 4, en/ar with RTL |
| **Frontend state** | Complete. **Only auth talks to the real API.** Every shop capability sits behind an abstract class bound to an in-memory implementation in `app.config.ts` |

The backend's own `CLAUDE.md` closes with: *"the sample feature was deleted. The catalogue domain has not been designed yet."* That is still true, and it is what this plan addresses.

### The two documents that govern everything

1. **`MangaStore\CLAUDE.md`** — how code must be written here. The controller rule, the service-returns-`Result` rule, AutoMapper-only mapping, Fluent API only, the 11-step feature checklist, XML docs on every public member. Every phase follows it without exception.
2. **`manga-store\docs\BACKEND-API-GUIDELINE.md`** — 610 lines written *for* this backend by whoever built the frontend, specifying entities, DTOs, routes, query names and error titles. It is the contract.

Where those two disagree with the frontend's actual TypeScript, **the TypeScript wins** — it is newer. Two places where that matters are called out below.

---

## Decisions taken before planning

| Question | Answer | Where it lands |
|---|---|---|
| Who owns the cart? | **Server-owned**, with `POST /cart/merge` on sign-in; the local cart stays for anonymous browsing | Phase 06 |
| Baseline branch? | **`origin/phase-01-foundation`**, not `main` | Phase 01 |
| How is localized content stored? | **Translation tables**, resolved via `Accept-Language`, DTOs returning one already-localized string | Phases 02, 03 |
| Optional scope | **All included** — wishlist, cover images, reviews, a dedicated seed phase | Phases 10, 14, 15, 04 |

---

## Cross-cutting rules

These recur across phases. Getting one wrong breaks the storefront in a way that is hard to trace back.

| # | Rule | Owner |
|---|---|---|
| 1 | `JsonStringEnumConverter` with camelCase, or every enum serialises as an integer and the client renders `stock.0` | 01 |
| 2 | `Money.Round` — half **away from zero**, not C#'s banker's rounding. 10% of $12.75 is $1.28 | 01 |
| 3 | `ResultError.Validation(entity, reason, message)` for 422s that need a title like `Coupon.Expired` | 01, 07 |
| 4 | Every `DateTime` leaves the API with a `Z`. The client ships `api-date.ts` purely because it does not today | 01 |
| 5 | A **bodyless** 401 means "refresh the token"; a 401 with a `ProblemDetails` body means a business error. Never use bare `Unauthorized()` or `Forbid()` | all |
| 6 | `IRepository<T>` cannot express the catalogue query. Bespoke methods on feature repositories — **no specification framework** | 01, 03 |
| 7 | `AppDbContext` is `NoTrackingWithIdentityResolution` globally. A write path must call `.AsTracking()` or use `ExecuteUpdate`, or the UPDATE silently never happens | 05, 06, 08 |
| 8 | Unique indexes must be **filtered** on `IsDeleted = 0`, or a soft-deleted row holds its slug forever | 02 and after |
| 9 | `productId`, never `mangaId`. The guideline's endpoint tables are stale; the TypeScript is not | 06, 08, 10 |
| 10 | The catalogue query parameter is `type`, though the TypeScript field is `kind`. It is in shared links | 03 |
| 11 | `IsActive` is backend-only. The frontend has no such concept, so an inactive product is **absent** from public responses, not flagged in them | 02, 03, 13 |
| 12 | Localized DTOs return **one string**. The TypeScript currently types them as `{ en, ar }`, so the swap needs a frontend change — say so, do not work around it | 03 |
| 13 | `Roles.Admin` and `Roles.Customer` are the whole authorization model. **No second mechanism** | 12, 13, 16 |
| 14 | Gift-card codes never appear in a public projection, a log line, a response body or a file on disk | 09 |
| 15 | Nothing pretends payment happened. Orders are `Pending` until a real cashier confirms | 08, 11 |

---

## The phases

| # | Name | Purpose | Branch |
|---|---|---|---|
| 01 | [Foundation](Phase-01-Foundation.md) | Enum converter, UTC timestamps, CORS and rate-limit repair, `CommerceOptions`, `Accept-Language` seam, `ResultError` overloads, persistence conventions | `phase-01-foundation` |
| 02 | [Catalogue Domain](Phase-02-Catalogue-Domain.md) | Product hierarchy, categories, authors, brands, volumes, translation tables. 16 tables, one migration. No endpoints | `phase-02-catalogue-domain` |
| 03 | [Catalogue Read API](Phase-03-Catalogue-Read-API.md) | Five public reads with search, six filters, five sorts, paging → **swaps `CatalogService`** | `phase-03-catalogue-api` |
| 04 | [Seed Data](Phase-04-Seed-Data.md) | Idempotent seeder: 12 categories, 10 authors, 30 manga, 14 gift cards, both languages | `phase-04-seed-data` |
| 05 | [Inventory](Phase-05-Inventory.md) | Atomic decrement, oversell prevention, the stock ledger, low/out-of-stock queries | `phase-05-inventory` |
| 06 | [Cart](Phase-06-Cart.md) | Server-owned cart, merge on sign-in, authoritative totals | `phase-06-cart` |
| 07 | [Coupons](Phase-07-Coupons.md) | Cart-wide and item-scoped discounts, seven rejection codes, the oracle rule | `phase-07-coupons` |
| 08 | [Orders](Phase-08-Orders.md) | Repricing, price snapshots, transactional stock commit, `Idempotency-Key` | `phase-08-orders` |
| 09 | [Gift Card Fulfilment](Phase-09-Gift-Card-Fulfilment.md) | Encrypted code pool, allocation on payment, admin import | `phase-09-gift-card-fulfilment` |
| 10 | [Wishlist](Phase-10-Wishlist.md) | One entity, three idempotent endpoints | `phase-10-wishlist` |
| 11 | [Payment Preparation](Phase-11-Payment-Preparation.md) | Gateway seam, `PaymentIntent`, the one path to `Paid`. **No provider, no webhook** | `phase-11-payment-preparation` |
| 12 | [Admin Dashboard](Phase-12-Admin-Dashboard.md) | Only statistics the schema can actually produce | `phase-12-admin-dashboard` |
| 13 | [Admin CRUD](Phase-13-Admin-CRUD.md) | Product, category and coupon management, order status, inventory adjustment | `phase-13-admin-crud` |
| 14 | [Media / Covers](Phase-14-Media-Covers.md) | Admin upload, content validation, content-addressed storage, static serving | `phase-14-media-covers` |
| 15 | [Reviews](Phase-15-Reviews.md) | Makes `averageRating` real. **The one phase with no frontend consumer** | `phase-15-reviews` |
| 16 | [Security & Testing](Phase-16-Security-Testing.md) | Whole-surface authorization sweep, secrets, the untested-component gaps, SQL Server CI | `phase-16-security-testing` |

---

## Dependencies

```text
01 Foundation
 └─ 02 Catalogue Domain
     ├─ 03 Catalogue Read API ──┬─ 10 Wishlist            (parallel-safe)
     │                          └─ 14 Media / Covers      (parallel-safe, after 13)
     ├─ 04 Seed Data                                      (parallel-safe)
     └─ 05 Inventory
         └─ 06 Cart
             └─ 07 Coupons
                 └─ 08 Orders
                     ├─ 09 Gift Card Fulfilment
                     │   └─ 11 Payment Preparation
                     │       └─ 13 Admin CRUD
                     ├─ 12 Admin Dashboard
                     └─ 15 Reviews
                         └─ 16 Security & Testing
```

Per phase:

| # | Depends on | Blocks | Independent? |
|---|---|---|---|
| 01 | — | everything | Yes |
| 02 | 01 | 03–16 | No |
| 03 | 01, 02 | 05, 06, 08, 10, 13, 14 | No |
| 04 | 01, 02 | nothing hard | Yes, after 02 |
| 05 | 01, 02 | 06, 08, 09, 12, 13 | No |
| 06 | 01, 02, 03, 05 | 07, 08 | No |
| 07 | 01, 02, 04, 06 | 08, 12, 13 | No |
| 08 | 01, 02, 05, 06, 07 | 09, 11, 12, 13 | No |
| 09 | 02, 05, 08 | 11, 12, 13 | Partly |
| 10 | 02, 03 | nothing | **Yes** |
| 11 | 01, 05, 08, 09 | 13 | No |
| 12 | 01, 02, 05, 07, 08, 09 | nothing | Partly |
| 13 | 02, 03, 05, 07, 08, 09, 11 | 14 | No |
| 14 | 02, 03, 13 | nothing | Mostly |
| 15 | 02, 03, 04, 08, 12 | nothing | No, but most deferrable |
| 16 | 01–15 | nothing | No |

---

## Implementation order

Sequential, following the dependency graph:

```text
01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 10 → 11 → 12 → 13 → 14 → 15 → 16
```

**Merge order is the same as implementation order.** Each phase branches from `main` after the previous one merged, so every branch starts from a state where its dependencies are already there.

### Where the value lands

Three phases change what a user can see. The rest are machinery.

| After | What goes live |
|---|---|
| **03** *(with 04)* | Home page, catalogue and product detail, on real data. `CatalogService` swaps and the entire sample catalogue is deleted. **This is the phase that pays** |
| **06** *(with 07)* | Cart and coupons become server-authoritative. `CartService` and `CouponService` swap |
| **08** | Order history at `/account/orders`. `OrderService` swaps. Checkout stays deliberately unreachable |

### Running work in parallel

- **Phase 10 (Wishlist)** needs only 02 and 03, blocks nothing, and touches nothing another phase touches. It is the best candidate for parallel work or a first contribution.
- **Phase 04 (Seed Data)** needs only 02 and can run alongside 03.
- **Phase 14 (Media)** needs 02, 03 and 13's authorization pattern, and touches only `CatalogController`'s new actions.
- **Phase 12 (Admin Dashboard)** can ship a reduced summary — catalogue and inventory sections only — after 05.

Everything from 05 to 08 is a chain. Do not try to parallelise it; each phase reads the previous one's invariants.

---

## Branch strategy

### The baseline is `phase-01-foundation`, not `main`

`origin/phase-01-foundation` is three commits ahead of `main` and adds real value:

- `/health/live` and `/health/ready`, split so a stale dependency stops traffic without making the orchestrator restart-loop a healthy process.
- A CI gate on EF model/migration drift.
- `ApiControllerBase.HandleOk(Result)` — 200 with no body, for a payment webhook ack.
- `ScopedBackgroundService` and `ResilienceDefaults`, both used by later phases.

It also carries two regressions that Phase 01 fixes: `Cors:AllowedOrigins` became `["*"]`, and rate limiting was deleted wholesale including the `/auth/*` policy the frontend README documents as live.

Discarding it would throw away working infrastructure to re-derive it later.

```text
main
 └── phase-01-foundation          ← existing; Phase 01 continues it, then merges to main
      ├── phase-02-catalogue-domain
      ├── phase-03-catalogue-api
      ├── phase-04-seed-data
      ├── phase-05-inventory
      ├── phase-06-cart
      ├── phase-07-coupons
      ├── phase-08-orders
      ├── phase-09-gift-card-fulfilment
      ├── phase-10-wishlist
      ├── phase-11-payment-preparation
      ├── phase-12-admin-dashboard
      ├── phase-13-admin-crud
      ├── phase-14-media-covers
      ├── phase-15-reviews
      └── phase-16-security-testing
```

Each branch is cut from `main` **after** its dependencies merged, not from the previous phase's branch. That keeps a phase reviewable on its own instead of as a diff against unmerged work.

### `docs/` is gitignored on the foundation branch

Line 114 of `.gitignore` on `phase-01-foundation` is `docs/`, added so an earlier set of phase docs stayed untracked. **These files would be untracked too.** Phase 01's first task is:

```gitignore
## Docs
docs/
!docs/backend-phases/
!docs/backend-phases/**
```

Leave the rest alone — the `service-account*.json`, `kashier*.local.json`, `keyencryption*.json` and gift-card-CSV patterns are load-bearing for Phases 09 and 11.

### Traces of an earlier plan

The foundation branch's `README.md` says the store is being built "in 14 documented phases" and links `docs/phases/00-overview-and-decisions.md`, which **exists in no branch**. Its `.gitignore` reserves Kashier merchant keys ("Phase 10"), an inventory key-encryption key ("Phase 05") and a Google Sheets service account ("Phase 06 FX").

So an earlier plan existed, involving Kashier as the cashier, an encrypted gift-card-key inventory, and a spreadsheet-driven exchange rate. **No code exists for any of it.** This plan covers the first two on their merits and leaves the third alone. Phase 11 records the provider choice as an open decision rather than assuming Kashier.

---

## Testing requirements

Every phase carries its own test list. These apply throughout.

### Standing rules

| Rule | Detail |
|---|---|
| Frameworks | Unit: xUnit + NSubstitute + **Shouldly**. Integration: `WebApplicationFactory<Program>` + SQLite in-memory. No FluentAssertions, no Moq, no Testcontainers |
| One database per test class | Tests must use distinct email addresses. `UniqueEmail(prefix)` exists for this |
| Parallelisation is off | `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in the integration assembly — two `WebApplicationFactory<Program>` instances racing inside `HostFactoryResolver`. Do not re-enable it |
| Build gate | `dotnet build -p:TreatWarningsAsErrors=true`. `GenerateDocumentationFile=true` makes a missing `<summary>` a **build error** |
| Drift gate | `dotnet ef migrations has-pending-model-changes` runs in CI |
| Mapper validity | `MapperConfiguration.AssertConfigurationIsValid()` in every phase that adds a profile |

### SQLite proves nothing about concurrency

`CustomWebApplicationFactory` runs SQLite in-memory. It does **not** implement `rowversion` and serialises writes differently from SQL Server.

Three guarantees depend on tests SQLite cannot run:

| Guarantee | Test | Phase |
|---|---|---|
| The shop cannot oversell | 20 concurrent reservations for 10 units → exactly 10 succeed | 05, 08 |
| A single-use coupon is redeemed once | 10 concurrent orders, `UsageLimit = 1` → exactly 1 succeeds | 08 |
| Rating aggregates cannot drift | 10 concurrent reviews → count 10, correct average | 15 |

Each is marked `[Trait("Category", "SqlServer")]` and excluded from the default run. **Phase 16 adds the CI job that runs them.** Until then, those guarantees are asserted and unproven.

`docker-compose.yml` already defines SQL Server 2022 with a health check.

### The frontend-contract tests

A handful of tests exist purely to stop the two applications drifting apart. They look fussy and they are the cheapest bugs in the plan to prevent:

- Enums serialise as camelCase strings, checked against **raw JSON** (Phases 01, 03, 08, 12).
- Timestamps carry a `Z` (01, 10).
- `releasedOn` is `YYYY-MM-DD`, not a timestamp (03).
- The pagination envelope has all seven fields (03).
- `ValidationService` produces `"Property: message; Property: message"` (16).
- Cart totals match a fixture taken from the frontend's own `pricing.spec.ts` (06).
- `CommerceOptions` defaults equal `CART_RULES` (01).

---

## Completion checklist

A phase is done when all of these are true. Individual phases add their own.

- [ ] Every acceptance criterion in the phase file is met.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds, with XML docs on every public member.
- [ ] `dotnet test` is green, including every new test the phase specifies.
- [ ] `dotnet ef migrations has-pending-model-changes` reports no drift.
- [ ] Any migration was reviewed as generated SQL, not just as C#, and applied against a real SQL Server.
- [ ] The `CLAUDE.md` 11-step checklist was followed for each new feature.
- [ ] Controllers do exactly three things; no mapping, validation or branching in an action.
- [ ] Services return `Result<T>` and never throw. Domain exceptions come only from Infrastructure.
- [ ] Every request DTO has a FluentValidation validator, invoked first in its service method.
- [ ] Every action declares `[ProducesResponseType<T>]` for each status it can return.
- [ ] Every new entity has a Fluent API configuration with `HasQueryFilter(e => !e.IsDeleted)`; no data annotations on any Domain entity.
- [ ] Nothing was registered manually that Scrutor can discover.
- [ ] Authorization attributes are per-action on any controller mixing public and admin actions.
- [ ] No frontend contract was broken; anything the frontend must change is named in the PR.
- [ ] Open questions raised by the phase are recorded in the PR, not left in someone's head.

---

## Open questions

Raised by the phases, none blocking, each needing a human:

| Question | Raised by |
|---|---|
| Which payment provider? Redirect or hosted fields? The `.gitignore` suggests Kashier; nothing else does | 11 |
| Trading currency. Every seeded price is `USD`; the brief's gift-card example is `4025 EGP`. The schema supports both; the seed and the frontend assume USD | 04, 11 |
| Should a cart of only gift cards be charged shipping? The frontend charges it on any non-empty cart, so the backend matches — but digital goods arguably ship free | 06 |
| Multiple cover widths need an imaging library. ImageSharp's licence is a business question; SkiaSharp is a heavier native dependency | 14 |
| Is the deployed Swagger and Scalar surface intentional? It currently publishes every admin route | 16 |
| Is there a real email sender? Password reset does not work without one, and reset links are currently written to the log | 16 |
