# Phase 12 — Admin Dashboard

**Recommended branch:** `phase-12-admin-dashboard`

---

## Objective

Expose the numbers an administrator needs to run the shop — and **only** the numbers the database can actually produce.

The brief lists seventeen candidate statistics. Most are derivable. A few are not, or are not what they sound like in this domain. This phase reports the real ones honestly and says plainly why the others are absent, rather than inventing a plausible figure.

---

## Current State

### Backend

No admin endpoint of any kind. The only role-guarded route in the repository is `AdminOnlyController` in the integration test project — a test double at `/api/v1/test-admin` that returns `{ ok = true }`, used to prove `[Authorize(Roles = Roles.Admin)]` produces a 403 with `ProblemDetails.Title == "Auth.Forbidden"`.

By this point the schema holds products, categories, orders, carts, coupons, stock movements, gift-card codes and payment intents. Everything below counts rows that exist.

### Frontend

**There is no admin area.** No route, no dashboard, no component, no translation key beyond `"Admin": "Admin"` as a role label. `roleGuard(...roles)` exists at `core/guards/role.guard.ts`, is exported, and is used by zero routes — the file's own comment says it is the seam for this.

So this phase ships a contract with no consumer. That is expected and stated in the guideline: *"Not built on the frontend yet, but the seam is ready."* It means the DTO shape is ours to choose well, and it means the tests are the only thing exercising it.

---

## Scope

Read-only aggregates. One controller, three endpoints.

| Component | Files |
|---|---|
| Application | `Features/Admin/Dashboard/` — `DashboardSummaryDto` and its nested records, `RecentOrderDto`, `RecentProductDto`, `IDashboardService` / `DashboardService`; a counting method added to `IIdentityService` |
| Infrastructure | Aggregate query methods on the existing repositories; `IdentityService` implementation of the new method |
| API | `AdminDashboardController` |

### Out of scope

- Every write. Phase 13 owns admin mutations.
- Time-series and charts. Nothing renders them, and a daily-revenue series is a different query shape that should be designed against a real chart.
- Export. No CSV, no report generation.

---

## Database Changes

**None.** Every figure below is an aggregate over existing tables. If a count turns out to need an index the earlier phases did not create, add it in a small migration here.

Two indexes already earn their keep: `Order (UserId, PlacedAt DESC)` from Phase 08 and `Product (Kind, IsActive, IsDeleted)` from Phase 02.

---

## API Contract

`AdminDashboardController : ApiControllerBase` with **`[Authorize(Roles = Roles.Admin)]` at class level**. Every action is admin-only and there is no anonymous action, so the class-level attribute is both safe and the right choice.

The route is overridden rather than inherited, because `[controller]` would produce `/api/v1/admindashboard`:

```csharp
[Route("api/v{version:apiVersion}/admin/dashboard")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminDashboardController : ApiControllerBase
```

An attribute route on a derived controller replaces the inherited one. Note the deviation from the `[controller]` convention in the class's XML doc so nobody "restores" it.

### `GET /admin/dashboard/summary`

| | |
|---|---|
| Auth | `[Authorize(Roles = Roles.Admin)]` |
| Success | `200` `DashboardSummaryDto` |
| Errors | `401`, `403` `ProblemDetails` with title `Auth.Forbidden` |

```jsonc
{
  "catalogue": {
    "totalProducts": 44,
    "byKind": { "manga": 30, "giftCard": 14 },
    "activeProducts": 44,
    "inactiveProducts": 0,
    "totalCategories": 12
  },
  "inventory": {
    "trackedProducts": 44,
    "inStock": 40,
    "lowStock": 3,
    "outOfStock": 1,
    "preOrder": 3
  },
  "orders": {
    "total": 128,
    "byStatus": { "pending": 12, "paid": 40, "shipped": 30, "delivered": 44, "cancelled": 2 }
  },
  "revenue": {
    "currency": "USD",
    "recognised": 8421.55,
    "pending": 340.20,
    "cancelled": 62.00
  },
  "customers": { "total": 87, "admins": 1 },
  "coupons": { "active": 4, "redemptions": 23 },
  "giftCardCodes": { "available": 210, "allocated": 40, "delivered": 38, "voided": 2 }
}
```

Nested rather than flat, so a caller can render one section without reading the whole shape, and so `byKind` and `byStatus` can gain a member without a breaking change when a new `ProductKind` or `OrderStatus` appears.

### `GET /admin/dashboard/recent-orders`

| | |
|---|---|
| Request | `take`, default `10`, clamped 1–50 |
| Success | `200` `RecentOrderDto[]` |

`{ id, reference, placedOn, status, total, currency, lineCount, customerEmail }`.

Newest first. `customerEmail` comes through `IIdentityService` — see below.

### `GET /admin/dashboard/recent-products`

| | |
|---|---|
| Request | `take`, default `10`, clamped 1–50 |
| Success | `200` `RecentProductDto[]` |

`{ id, slug, title, kind, price, currency, stockStatus, isActive, createdAt }`.

Ordered by `CreatedAt` descending — **recently added**, which is different from `ReleasedOn`. A back-catalogue title added yesterday belongs at the top of this list and nowhere near the storefront's "new arrivals".

Includes inactive products. This is an admin view; a freshly added product that has not been activated yet is exactly what an admin wants to see.

---

## Business Rules

### What each figure means

Ambiguity here produces numbers that are wrong in ways nobody notices, so every one is defined.

| Figure | Definition |
|---|---|
| `totalProducts` | Not soft-deleted. Includes inactive |
| `byKind` | Same population, grouped by `ProductKind` |
| `activeProducts` / `inactiveProducts` | Split of the above on `IsActive`. They sum to `totalProducts` |
| `totalCategories` | Not soft-deleted. Includes categories with no products |
| `trackedProducts` | `InventoryMode.Tracked`, not deleted |
| `inStock` / `lowStock` / `outOfStock` / `preOrder` | Derived with the **same** `StockStatus.Derive` the catalogue uses. Over all non-deleted products, active or not. `Unlimited` products land in `inStock`. The four sum to `totalProducts` |
| `orders.total` | Not soft-deleted. All statuses |
| `byStatus` | Grouped by `OrderStatus`. Sums to `orders.total` |
| `revenue.recognised` | Sum of `Total` for `Paid`, `Shipped`, `Delivered` |
| `revenue.pending` | Sum of `Total` for `Pending` — **money not received** |
| `revenue.cancelled` | Sum of `Total` for `Cancelled`. Reported because a rising figure is a signal |
| `customers.total` | All accounts, via `IIdentityService` |
| `customers.admins` | Accounts in `Roles.Admin` |
| `coupons.active` | `IsActive`, not deleted, and currently within its window |
| `coupons.redemptions` | `CouponRedemption` row count |
| `giftCardCodes.*` | Counts by `GiftCardCodeStatus` |

### Revenue will be zero, and that is correct

Nothing reaches `Paid` until Phase 11 has a real cashier. Until then `recognised` is `0` and every order sits in `pending`.

**Do not compensate.** Do not count `Pending` as revenue, do not add a "projected" figure, and do not seed fake orders to make the dashboard look populated. A dashboard reporting revenue the shop has not received is worse than one reporting zero, because zero is true and someone will act on it.

### Currency

Every figure assumes one trading currency, taken from `CommerceOptions.DefaultCurrency`. Orders carry their own currency, and summing across currencies would be meaningless.

Guard it: if more than one distinct `Order.Currency` exists, the revenue block must not silently sum them. Report the default currency's total and log a Warning. Multi-currency reporting needs exchange rates, a rate source and an as-at date — none of which exist, and none of which should be invented here.

### Statistics from the brief that are **not** included

Say why, rather than quietly dropping them.

| Requested | Status |
|---|---|
| "Total digital products" | **Reported as `byKind.giftCard`.** In this domain the only digital product type is the gift card — `ProductKind` has exactly two members. A separate "digital products" count would either duplicate `giftCard` or invent a third kind that nothing sells |
| "Processing orders" | **Not a status.** `OrderStatus` is `Pending`, `Paid`, `Shipped`, `Delivered`, `Cancelled`, matching the frontend's `OrderStatus` type exactly. Adding `Processing` would mean a new enum member, a client translation key and a transition rule, for a state no part of the system distinguishes |
| "Completed orders" | **Reported as `byStatus.delivered`.** "Completed" is not a status; `Delivered` is the terminal success state |

Adding `Processing` later is a small change. Reporting a number under a name the schema does not have is not a small problem.

### Counting users has to go through the Identity seam

`ApplicationUser` lives in Infrastructure and Application must never see it — that is the seam `CLAUDE.md` names explicitly: *"Need a user operation from a service? Add a method to `IIdentityService`. Don't inject `UserManager`."*

So `DashboardService` cannot query the users table. Add to `IIdentityService`:

```csharp
/// <summary>Counts accounts, optionally restricted to a role.</summary>
/// <param name="role">Role name to filter by, or <see langword="null"/> for every account.</param>
Task<int> CountUsersAsync(string? role = null, CancellationToken ct = default);

/// <summary>Returns email addresses for the given account ids, for admin display.</summary>
Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(
    IReadOnlyList<Guid> userIds, CancellationToken ct = default);
```

`GetEmailsAsync` exists because `RecentOrderDto.customerEmail` needs it and `Order` stores only a bare `UserId` — deliberately, following `RefreshToken`'s precedent. Batch the lookup rather than resolving per order; ten orders should be one query, not ten.

### Query cost

The summary is roughly fifteen aggregates. Two constraints:

- **One `DbContext` is not thread-safe.** Do not `Task.WhenAll` these against a shared context. Run them sequentially, or compose them into a single projection with scalar subqueries.
- **Prefer one query.** A single `SELECT` with fifteen correlated scalar subqueries is one round trip and is well within what SQL Server optimises. Fifteen sequential round trips is fifteen times the latency for a page an admin refreshes.

No caching in this phase. At the scale in evidence — tens of products, hundreds of orders — the query is cheap, and a stale dashboard is its own class of confusion. If it becomes slow, the seam is an `IMemoryCache` wrapper with a 30-second entry in `DashboardService`, and it should be added with a measurement rather than in anticipation.

> `AddOutputCache()` was registered in the template and removed on the foundation branch. Do not reinstate it for this. Output caching an admin-only endpoint keyed by nothing would serve one admin's view to another; if caching is added it belongs inside the service, keyed explicitly.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Class-level `[Authorize(Roles = Roles.Admin)]`. |
| Authorization | Role-based, reusing the existing mechanism. **No second authorization system.** The brief is explicit and so is `CLAUDE.md`. |
| Role checks | `Roles.Admin` only. `Roles.Customer` gets 403 with `ProblemDetails.Title == "Auth.Forbidden"`, produced by the existing `ProblemDetailsAuthorizationResultHandler`. |
| Validation | `take` clamped 1–50 in a validator, not just documented. |
| Sensitive data | **This is the real concern.** See below. |
| Concurrency | Reads only. Figures are a snapshot and may be individually consistent but collectively a few milliseconds apart. Acceptable for a dashboard; do not wrap it in a serialisable transaction to make the numbers agree to the instant. |
| Rate limiting | The global policy. |

### What the dashboard reveals

Aggregates are more sensitive than they look. This one exposes revenue, sales volume, customer count and gift-card pool depth — a competitor's ideal summary and a fraudster's map of which cards are worth attacking.

Three rules:

1. **`Roles.Admin` and nothing less.** No "read-only analyst" role, because there is no such role and adding one is a change to the authorization model, not to a dashboard.
2. **`giftCardCodes` reports counts by status and nothing else.** No sample code, no id, no batch listing. Phase 09's rule holds everywhere.
3. **`customerEmail` on recent orders is the only personal data here.** It is needed — an admin looking at an order needs to know whose it is — but it must not appear in a log line, and there must be no endpoint that lists customers. `GetEmailsAsync` takes explicit ids for exactly that reason: there is no way to ask it for "all emails".

### Not to add

- A customer list or search. Not requested, not in the frontend, and a customer-enumeration endpoint is a liability that has to be justified before it is built.
- A "top customers by spend" figure. Same reason.
- Anything that returns a gift-card code, at any status, for any reason.

---

## Frontend Contract

**Nothing consumes this yet.** There is no admin route in the Angular app.

The seam that exists: `core/guards/role.guard.ts`, unused, waiting for `canActivate: [roleGuard(ROLES.admin)]` on a future `/admin` route. `AuthService.isAdmin` is a computed signal that already reads the `roles` array on `User`.

When the dashboard UI is built, everything it needs is present: the role claim is in the token, `roleGuard` gates the route, `/forbidden` exists as a landing page, and `errorInterceptor` already passes 403 through for a page to render inline.

Two shape decisions made with that future UI in mind:

- **Nested sections**, so a card component can take one section rather than destructuring a flat object of twenty fields.
- **`byKind` and `byStatus` as objects keyed by the camelCase enum value**, so the UI can iterate them and build translation keys the same way it already does for `stock.*` and `orders.status.*`. A new status appears in the UI without a code change.

---

## Testing

### Unit tests

Substitute the repositories and `IIdentityService`.

| Test | Asserts |
|---|---|
| `DashboardServiceTests.ByKindSumsToTotalProducts` | The invariant that catches a filter mismatch between two counts. |
| `DashboardServiceTests.ActivePlusInactiveEqualsTotal` | |
| `DashboardServiceTests.StockStatusCountsSumToTotalProducts` | All four buckets, `Unlimited` products landing in `inStock`. |
| `DashboardServiceTests.ByStatusSumsToTotalOrders` | |
| `DashboardServiceTests.RecognisedRevenueExcludesPendingAndCancelled` | **The definition test.** |
| `DashboardServiceTests.PendingRevenueIsReportedSeparately` | Not folded into recognised. |
| `DashboardServiceTests.NoOrders_ReportsZerosNotNulls` | An empty shop renders `0`, not `null`. |
| `DashboardServiceTests.MixedCurrencies_LogsWarningAndDoesNotSumAcross` | |
| `DashboardServiceTests.UserCountsComeFromIdentityService` | The repository is never asked for users. **Pins the seam.** |
| `DashboardServiceTests.RecentOrderEmails_AreResolvedInOneBatchCall` | Ten orders, one call to `GetEmailsAsync`. |
| `DashboardServiceTests.StockCountsUseTheSameDeriveAsTheCatalogue` | Same helper, same thresholds — no second implementation. |

### Integration tests

| Test | Asserts |
|---|---|
| `AdminDashboardApiTests.Anonymous_Returns401` | All three endpoints. |
| `AdminDashboardApiTests.Customer_Returns403WithAuthForbidden` | `ProblemDetails.Title == "Auth.Forbidden"`, matching what `AuthApiTests` already asserts for the test-admin double. |
| `AdminDashboardApiTests.Admin_Returns200` | With the seeded admin. |
| `AdminDashboardApiTests.SummaryMatchesSeededCatalogue` | Against Phase 04's seed: 44 products, 30 manga, 14 gift cards, 12 categories. **Ties the dashboard to real data.** |
| `AdminDashboardApiTests.EnumKeysAreCamelCase` | Raw JSON `"manga"`, `"giftCard"`, `"pending"` — never `"0"`. Dictionary **keys** are not covered by `JsonStringEnumConverter` the way values are; if the key comes out as `"0"` the fix is a `JsonConverter` on the dictionary or a `string`-keyed DTO. Worth testing precisely because it is the one place Phase 01's converter does not automatically apply. |
| `AdminDashboardApiTests.NoResponseContainsAGiftCardCode` | Sweep all three payloads. |
| `AdminDashboardApiTests.RecentProductsIncludeInactive` | |
| `AdminDashboardApiTests.TakeIsClamped` | `?take=9999` returns at most 50. |
| `AdminDashboardApiTests.RevenueIsZeroBeforeAnyPayment` | The honest-zero test. |

### Edge cases

- Empty database: every figure `0`, no division, no null.
- One order in each status: `byStatus` has five members, all `1`.
- A product with `InventoryMode.Unlimited` and `StockQuantity = 0`: counted as `inStock`.
- A soft-deleted product: excluded from every count.
- A category with no products: counted in `totalCategories`.
- An order whose customer account was deleted: `customerEmail` is `null`, not an exception. Accounts are not deleted today, but the dashboard must not be the thing that breaks when they are.
- `take=0`: clamped to 1.

---

## Acceptance Criteria

- [ ] `AdminDashboardController` at `/api/v1/admin/dashboard` with class-level `[Authorize(Roles = Roles.Admin)]` and the route deviation documented.
- [ ] Three actions, each one line, each with `[ProducesResponseType<T>]` including `403` as `ProblemDetails`.
- [ ] `DashboardSummaryDto` nested into `catalogue`, `inventory`, `orders`, `revenue`, `customers`, `coupons`, `giftCardCodes`.
- [ ] Every figure matches the definitions table; the sum invariants hold.
- [ ] `revenue.recognised` covers `Paid`, `Shipped`, `Delivered` only; `pending` and `cancelled` reported separately.
- [ ] Revenue reports **zero** before any payment exists, with no projected or compensating figure.
- [ ] Multi-currency data logs a Warning and does not sum across currencies.
- [ ] `IIdentityService.CountUsersAsync` and `GetEmailsAsync` added; `DashboardService` never touches `UserManager` or the users table.
- [ ] Emails resolved in one batched call.
- [ ] Stock counts use the same `StockStatus.Derive` as the catalogue — no second implementation.
- [ ] Enum-keyed dictionaries serialise as camelCase strings, proved by a raw-JSON test.
- [ ] `take` clamped 1–50 by a validator.
- [ ] No gift-card code appears in any response; no customer-listing endpoint was added.
- [ ] "Processing" and "Completed" are **not** invented; the omission is documented.
- [ ] The summary is one round trip, or sequential — never `Task.WhenAll` on a shared `DbContext`.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 02 - Product, Category.
  Phase 05 - StockStatus.Derive and inventory columns.
  Phase 07 - Coupon, CouponRedemption.
  Phase 08 - Order, OrderStatus.
  Phase 09 - GiftCardCode statuses.
  Phase 01 - CommerceOptions.DefaultCurrency.

Blocks:
  Nothing. No later phase reads the dashboard.

Can be implemented independently:
  Partly. Each section only needs its own phase, so a reduced summary can
  ship earlier - catalogue and inventory sections need only Phases 02 and 05.
  Shipping it whole is simpler, and it is the last phase that reads across
  every other one, which makes it a useful integration check in its own right.
```
