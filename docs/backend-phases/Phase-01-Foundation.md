# Phase 01 — Foundation

**Recommended branch:** `phase-01-foundation` *(already exists on `origin`; this phase continues it)*

---

## Objective

Make the existing template able to carry a catalogue and a commerce domain, and fix the things that would silently break the Angular storefront the moment the first shop endpoint ships.

Nothing user-facing is added. Every change here is a seam, a convention, or a repair. If this phase is done correctly, Phases 02–16 can each be written without stopping to invent shared plumbing.

---

## Current State

### What exists

The repository is a working **auth-only** Clean Architecture template on .NET 10.

| Layer | What is there |
|---|---|
| Domain | `BaseEntity`, `IAuditableEntity`, `DomainEvent`/`IDomainEvent`, `NotFoundException`/`ConflictException`/`ForbiddenException`, `IRepository<T>`, `IUnitOfWork`, `IDateTime`, `RefreshToken` + `IRefreshTokenRepository`, `Roles` (`Customer`, `Admin`) |
| Application | `Result`/`Result<T>`/`ResultError`/`ResultErrorCodes`, `PaginatedList<T>`, `PaginationParams`, `IValidationService`/`ValidationService`, `ICurrentUser`, `IIdentityService`, `ITokenService`, `AppClaimTypes`, `AppUserInfo`, `IEmailSender`, `IDomainEventDispatcher`/`IDomainEventListener<T>`, `JwtOptions`, `AppOptions`, `Features/Auth/*`, `Features/Users/*` |
| Infrastructure | `AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, `RefreshTokenConfiguration`, `GenericRepository<T>`, `RefreshTokenRepository`, `UnitOfWork`, `DomainEventDispatcher`, `AuditInterceptor`, `SoftDeleteInterceptor`, `IdentityService`, `IdentitySeeder`, `JwtTokenService`, `LoggingEmailSender`, `SystemDateTime`, `HealthCheckTags`, `ScopedBackgroundService`, `ResilienceDefaults` |
| API | `ApiControllerBase`, `AuthController`, `UsersController`, `GlobalExceptionHandler`, `ProblemDetailsAuthorizationResultHandler`, `CurrentUser`, `CorsOptions`, `Program.cs` |
| Database | **One** migration (`20260816192522_InitialCreate`), **8** tables: the 7 ASP.NET Identity tables plus `RefreshTokens` |
| Tests | `MangaStore.UnitTests` (29 tests: `Result`, `PaginationParams`, `AuthService`), `MangaStore.IntegrationTests` (`AuthApiTests` 14 facts, `HealthCheckTests` 3 facts) |

### What this branch already added over `main`

- `/health/live` (never fails on a dependency) and `/health/ready` (checks tagged `ready`), replacing the single `/health`.
- CI step `dotnet ef migrations has-pending-model-changes` — a model/migration drift gate.
- `ApiControllerBase.HandleOk(Result)` — 200 with no body, for a PSP webhook ack.
- `ScopedBackgroundService` — scope per tick, per-tick exception isolation, jitter off the injected `TimeProvider`. No subclass yet.
- `ResilienceDefaults` — a `ReadOnlyExternal` profile that retries and a `NonIdempotentExternal` profile that does not, because retrying a payment-session create is a duplicate charge. No consumer yet.
- SQLite confined to the integration test project; `AddPersistence` is SQL Server only.
- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in the integration assembly.

### What is missing or wrong

| Gap | Consequence |
|---|---|
| No `JsonStringEnumConverter` | Every enum serialises as an integer. The storefront builds translation keys from the string form. |
| `Cors:AllowedOrigins` is `["*"]` on this branch | Wide open, and incompatible with `AllowCredentials` if it is ever needed. The Angular dev server is on **port 3000**. |
| Rate limiting deleted on this branch | The `/auth/*` 10-req/min per-IP policy the storefront README documents as live is gone. |
| Timestamps serialise without a `Z` designator | The client ships `core/utils/api-date.ts` purely to append one. |
| `ResultError.Validation(message)` hard-codes `Title = "Validation"` | Coupon rejections cannot carry `Coupon.NotFound` and friends, which is what the client maps on. |
| No commerce configuration | Shipping threshold, flat rate, tax rate, express surcharge and max line quantity exist only as frontend constants. |
| No `Accept-Language` resolution | Localized catalogue content has nowhere to read the requested language from. |
| `IRepository<T>` has no filtering or sorting | The catalogue list endpoint needs search plus six filters, five sorts and paging. |
| No decimal or `DateOnly` conventions | Money would land as `decimal(18,2)` only where someone remembers to write it. |
| `.gitignore` line 114 ignores `docs/` | These phase files would be untracked. |

---

## Scope

1. **JSON serialisation** — register `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` and confirm camelCase property naming.
2. **UTC timestamps** — every `DateTime` leaves the API with a `Z`.
3. **CORS repair** — replace `["*"]` with the real origin list.
4. **Rate limiting restored** — reinstate `RateLimitOptions`, the `fixed` and `auth` policies, and `[EnableRateLimiting]` on `AuthController`.
5. **`CommerceOptions`** — the pricing constants, bound and validated.
6. **`Accept-Language` seam** — `IRequestLanguage` in Application, implemented in API.
7. **`ResultError.Validation` overloads** — a 422 that can carry an entity-qualified title.
8. **Persistence conventions** — `decimal(18,2)` for money, `DateOnly` mapping, filtered unique indexes, sequential GUID keys.
9. **Repository query approach** — documented, not built: feature repositories get bespoke methods; no specification framework.
10. **`.gitignore`** — un-ignore `docs/backend-phases/`.

### Out of scope

No entity, no `DbSet`, no migration, no controller, no service. This phase adds no table and no endpoint.

---

## Database Changes

**None.** No entity is added, so no migration is produced. Two *conventions* are registered on `AppDbContext` that will apply to every entity added from Phase 02 onward:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
{
    // Money is decimal(18,2) everywhere, so no configuration can forget it.
    builder.Properties<decimal>().HavePrecision(18, 2);

    // DateOnly maps to `date`, not `datetime2`. ReleasedOn is a calendar day,
    // not an instant, and the client sends and expects "YYYY-MM-DD".
    builder.Properties<DateOnly>().HaveColumnType("date");
}
```

> **Verify the drift gate stays green.** `ConfigureConventions` changes the model even with no new entities, and CI runs `dotnet ef migrations has-pending-model-changes`. If the existing `RefreshToken` mapping shifts, generate the migration in this phase rather than leaving Phase 02 to explain a change it did not make.

### `BaseEntity.Id` — sequential GUIDs

`Guid.NewGuid()` produces a random v4. As a clustered SQL Server primary key that fragments the index on every insert. .NET 9+ ships `Guid.CreateVersion7()`, which is time-ordered.

```csharp
public Guid Id { get; protected set; } = Guid.CreateVersion7();
```

One line, real payoff once the catalogue holds tens of thousands of rows. It does **not** require a migration — the column type is unchanged — and existing rows are unaffected.

---

## API Contract

**No new endpoints.** Two behavioural changes affect every existing and future endpoint.

### Enum serialisation

```csharp
services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
```

The storefront builds translation keys by concatenation — `'stock.' + stockStatus`, `'orders.status.' + status`. These exact strings are required:

| Enum | Wire values |
|---|---|
| `ProductKind` | `manga`, `giftCard` |
| `StockStatus` | `inStock`, `lowStock`, `preOrder`, `outOfStock` |
| `OrderStatus` | `pending`, `paid`, `shipped`, `delivered`, `cancelled` |
| `ShippingMethod` | `standard`, `express` |
| `DeliveryType` | `instantEmail`, `accountCredit` |
| `ReadingDirection` | `rightToLeft`, `leftToRight` |
| `CouponScope` | `cart`, `item` |

An integer here does not throw. It renders a missing translation key in the UI, which is exactly the kind of failure that survives a code review.

### Timestamps

`UserDto.CreatedAt` currently serialises as `2026-08-18T22:04:21.5843337` — no designator — while `AuthResponse.AccessTokenExpiresAt` carries a `Z`. The client's `core/utils/api-date.ts` appends `Z` when none is present, and exists solely for this.

The cause is `DateTimeKind.Unspecified`: `AuditInterceptor` and `IdentityService` write `IDateTime.UtcNow`, which is `_timeProvider.GetUtcNow().UtcDateTime` (kind `Utc`), but EF Core reads `datetime2` back as `Unspecified`.

Fix at the boundary, so no future DTO can regress:

```csharp
/// <summary>Serialises every DateTime as UTC with a Z designator.</summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    /// <inheritdoc/>
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
```

Register it alongside the enum converter, plus a `JsonConverter<DateTime?>` sibling for nullable columns such as `UpdatedAt`.

> Record this in the phase-completion note: once this ships, `api-date.ts` can be deleted on the frontend. Leaving it is harmless — appending `Z` to a string that already ends in `Z` is guarded — but it is dead weight that will confuse the next reader.

---

## Business Rules

### `CommerceOptions`

`manga-store\src\app\core\catalog\models\cart.model.ts` holds these as `CART_RULES`. The guideline (§5.3) is explicit that they are placeholders and belong in configuration. The backend becomes the authority; the frontend's copy becomes a display default only.

```csharp
namespace MangaStore.Application.Common.Options;

/// <summary>Pricing rules the shop applies to every cart and order.</summary>
public sealed class CommerceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Commerce";

    /// <summary>ISO 4217 code used when a cart has no line to take a currency from.</summary>
    [Required, StringLength(3, MinimumLength = 3)]
    public string DefaultCurrency { get; init; } = "USD";

    /// <summary>Subtotal at or above which delivery is free.</summary>
    [Range(0, 100000)]
    public decimal FreeShippingThreshold { get; init; } = 50m;

    /// <summary>Delivery charge below the free-shipping threshold.</summary>
    [Range(0, 10000)]
    public decimal ShippingFlatRate { get; init; } = 4.99m;

    /// <summary>Added to delivery when express is chosen.</summary>
    [Range(0, 10000)]
    public decimal ExpressSurcharge { get; init; } = 7.50m;

    /// <summary>Tax applied to the post-discount amount. 0.14 is 14%.</summary>
    [Range(0, 1)]
    public decimal TaxRate { get; init; } = 0.14m;

    /// <summary>Maximum quantity on a single cart line, matching the client's stepper.</summary>
    [Range(1, 1000)]
    public int MaxLineQuantity { get; init; } = 10;

    /// <summary>Stock at or below which a product reports lowStock.</summary>
    [Range(0, 10000)]
    public int LowStockThreshold { get; init; } = 5;
}
```

Bind it in `AddApplication` (or `AddInfrastructure`, beside `JwtOptions`) with `.ValidateDataAnnotations().ValidateOnStart()`, matching how `JwtOptions` and `AppOptions` are already registered, and add the `Commerce` section to `appsettings.json`.

### Money rounding — read this before writing any arithmetic

The frontend rounds with `Math.round(value * 100) / 100`, which is **half away from zero** for positive values. C#'s `Math.Round(value, 2)` defaults to **banker's rounding** (`MidpointRounding.ToEven`).

`pricing.model.ts` documents the exact case, because it was a real bug there:

> 10% of $12.75 is $1.275. `Math.Round(1.275m, 2)` gives **1.27**; `Math.Round(1.275m, 2, MidpointRounding.AwayFromZero)` gives **1.28**. The exact answer is 1.275, and the customer-facing correct result is **1.28**.

Provide one helper and use nothing else for money:

```csharp
namespace MangaStore.Application.Common;

/// <summary>Rounding used for every customer-facing amount.</summary>
public static class Money
{
    /// <summary>Rounds to cents, half away from zero, matching the storefront's arithmetic.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
```

A discount that disagrees by a cent between the cart line and the order summary is a support ticket, and it is the kind of defect that only appears for particular prices.

### Language resolution

Catalogue content is stored in translation tables from Phase 02 and returned as **one already-localized string**. Services need to know which language was asked for, and Application cannot see `HttpContext`.

Mirror the existing `ICurrentUser` seam exactly — interface in Application, implementation in API:

```csharp
// Application/Common/Localization/IRequestLanguage.cs
/// <summary>The language the current request asked for, resolved from Accept-Language.</summary>
public interface IRequestLanguage
{
    /// <summary>Two-letter code of the requested language, or the default when none was supplied or supported.</summary>
    string Code { get; }
}

// Application/Common/Localization/SupportedLanguages.cs
/// <summary>Languages the catalogue carries translations for.</summary>
public static class SupportedLanguages
{
    /// <summary>Used when the request asks for a language that has no translation.</summary>
    public const string Default = "en";

    /// <summary>Every supported code.</summary>
    public static IReadOnlyList<string> All { get; } = ["en", "ar"];
}
```

The API implementation reads `Accept-Language` through `IHttpContextAccessor`, takes the highest-weighted supported match, and falls back to `SupportedLanguages.Default`. Register it scoped, beside `ICurrentUser`.

> Do not add `AddRequestLocalization`. Nothing in this API formats numbers or dates server-side, and `CultureInfo.CurrentUICulture` would become a second source of truth for the same question. One explicit seam is easier to reason about and easier to test.

### `ResultError.Validation` gains entity-qualified overloads

`ApiControllerBase.MapErrorToResponse` maps `ResultErrorCodes.Validation` to 422. Today the only factory is:

```csharp
public static ResultError Validation(string message) =>
    new(ResultErrorCodes.Validation, ResultErrorCodes.Validation, message);
```

The title is fixed to the literal `"Validation"`. That is right for FluentValidation failures, which are framework-aggregated and have no entity. It is wrong for a coupon rejection: the guideline specifies 422 with a title of `Coupon.NotFound`, `Coupon.Expired`, `Coupon.MinimumNotReached` and four others, and `ErrorMessageService` on the client maps **on the title** to pick its sentence.

Add two overloads and leave the existing one untouched:

```csharp
/// <summary>Creates a 422-mapped error qualified by an entity.</summary>
/// <param name="entity">Entity or resource name used to qualify the title.</param>
/// <param name="message">Human-readable description of the failure.</param>
public static ResultError Validation(string entity, string message) =>
    new(ResultErrorCodes.Validation, $"{entity}.{ResultErrorCodes.Validation}", message);

/// <summary>Creates a 422-mapped error with a fully specified title, for domain rules with their own vocabulary.</summary>
/// <param name="entity">Entity or resource name used to qualify the title.</param>
/// <param name="reason">Title suffix, e.g. <c>"Expired"</c>, producing <c>"Coupon.Expired"</c>.</param>
/// <param name="message">Human-readable description of the failure.</param>
public static ResultError Validation(string entity, string reason, string message) =>
    new(ResultErrorCodes.Validation, $"{entity}.{reason}", message);
```

Phase 07 uses the three-argument form for all seven coupon rejections.

### CORS

Restore the origin list. The Angular dev server runs on **port 3000** (`angular.json` → `serve.options.port`), which is also what `App:FrontendBaseUrl` uses when building password-reset links. Port 4200 is the Angular default and is worth keeping for anyone who overrides the port.

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:3000",
    "http://localhost:4200",
    "https://mangastore.runasp.net"
  ]
}
```

Keep `CorsOptions.AnyOrigin` and the `AllowAnyOrigin()` branch if you like — a wildcard is defensible for a public read-only catalogue — but the committed default must not be `["*"]`. There are `[Authorize]` endpoints on the same origin policy.

> The current policy does **not** call `AllowCredentials()`, and must not: the refresh token is returned in the response body and held in `localStorage`, not in a cookie. `AllowAnyOrigin()` and `AllowCredentials()` are mutually exclusive in ASP.NET Core anyway.

### Rate limiting

Reinstate `src/MangaStore.API/Options/RateLimitOptions.cs` and its registration, deleted in commit `f49081f`.

| Policy | Partition | Limit | Applied to |
|---|---|---|---|
| `fixed` (`RateLimitOptions.DefaultPolicy`) | global | `PermitLimit` = 100 / 60s | global limiter |
| `auth` (`RateLimitOptions.AuthPolicy`) | client IP | `AuthPermitLimit` = 10 / 60s | `[EnableRateLimiting]` on `AuthController` |

`RejectionStatusCode = 429`. The client's `error.interceptor.ts` auto-toasts 429, so a rejection is already handled end to end.

Restore `"RateLimit": { "WindowSeconds": 60, "PermitLimit": 100 }` in `appsettings.json`, and re-add the high test limits to `CustomWebApplicationFactory` (`RateLimit:PermitLimit=1000`, `RateLimit:AuthPermitLimit=1000`) so the auth integration tests do not trip the limiter.

> Phase 07 adds an `auth`-style policy for `POST /cart/coupons`. A coupon endpoint that answers unlimited guesses is an oracle for discovering live campaign codes. The policy is defined here; the attribute goes on in Phase 07.

### Repository queries — the approach, not the code

`IRepository<T>` exposes `GetByIdAsync`, `GetAllAsync`, `GetPagedAsync(skip, take)`, `CountAsync`, `AddAsync`, `Delete`, `ExistsAsync`. There is no filtering, no sorting, no include, no projection. `GET /catalog/products` needs a text search across two languages, six filters, five sort orders and paging. `GetAllAsync` followed by LINQ-to-objects would pull the whole catalogue into memory on every request.

**Decision: bespoke methods on feature repositories.** `CLAUDE.md` step 3 already says `IXxxRepository extends IRepository<T>`, so this is the existing idiom:

```csharp
// Domain/Features/Catalogue/IProductRepository.cs
public interface IProductRepository : IRepository<Product>
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        ProductQuery query, string languageCode, CancellationToken ct = default);
}
```

`ProductQuery` is a Domain-level parameter object — not a DTO, because Domain cannot reference Application. Phase 03 defines it.

**Rejected: a specification pattern.** It would mean a new abstraction, an evaluator in Infrastructure, and a second way to express a query alongside the repository methods that already exist — for one endpoint. `CLAUDE.md` says "when in doubt, put it in Application, not Domain", and adding a framework is the opposite of that.

Do **not** widen `IRepository<T>` with `IQueryable` or predicate overloads. Returning `IQueryable` from a repository lets EF Core leak into Application, which the dependency rule forbids.

### Change tracking — a trap to document

`AppDbContext`'s constructor sets:

```csharp
ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTrackingWithIdentityResolution;
```

Every query is no-tracking **by default**. `GenericRepository.GetByIdAsync` calls `.AsTracking()` explicitly; `GetAllAsync`, `GetPagedAsync` and the rest call `.AsNoTracking()`.

Any new query method whose result will be **mutated and saved** must call `.AsTracking()`. This bites in:

- **Phase 05** — stock decrement.
- **Phase 06** — cart line quantity updates.
- **Phase 08** — order placement, which loads products both to reprice and to decrement.

A no-tracking entity mutated and passed to `SaveChangesAsync` produces no UPDATE and no error. Repeat this warning in each of those phases, and prefer `ExecuteUpdateAsync` for the atomic stock decrement, which sidesteps the question entirely.

### Soft delete and unique indexes

Every entity gets `HasQueryFilter(e => !e.IsDeleted)`. Combined with a unique index on `Slug`, a soft-deleted product blocks its slug forever — the filter hides the row from queries, but the index still sees it.

Use a filtered index:

```csharp
builder.HasIndex(p => p.Slug)
       .IsUnique()
       .HasFilter("[IsDeleted] = 0");
```

> SQLite, used by the integration tests, supports partial indexes through the same `HasFilter` call, so this does not diverge between providers.

Applies to `Product.Slug`, `Category.Slug`, `Coupon.Code`, `MangaDetail.Isbn`, `Order.Reference`, and the composite uniques on `CartItem` and `WishlistItem`.

### `.gitignore`

Line 114 of `.gitignore` on this branch is `docs/`, added so the earlier phase docs stayed untracked. These files are the deliverable and must be committed:

```gitignore
## Docs
docs/
!docs/backend-phases/
!docs/backend-phases/**
```

Leave the rest of the ignore file alone. In particular the secret-material blocks — `service-account*.json`, `kashier*.local.json`, `keyencryption*.json`, and the gift-card-key CSV patterns — stay exactly as they are; Phase 09 and Phase 11 depend on them.

---

## Security

| Concern | This phase |
|---|---|
| Authentication | Unchanged. JWT bearer, `MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`, `ClockSkew = Zero`. **Do not touch any of these** — changing one makes `[Authorize(Roles = ...)]` stop matching silently. |
| Authorization | Unchanged. Role-based via `[Authorize(Roles = ...)]`; no named policies. |
| CORS | Tightened from `["*"]` to an explicit origin list. No `AllowCredentials`. |
| Rate limiting | Restored: 100/min globally, 10/min per IP on `/auth/*`. |
| Validation | `SuppressModelStateInvalidFilter = true` stays — FluentValidation through `IValidationService` is the single validation path. |
| Sensitive data | No new surface. `LoggingEmailSender` still writes reset links to the log; that is a development-only sender and Phase 16 revisits it. |
| Concurrency | Not yet applicable; Phase 05 introduces the concurrency token. |

### The 401 distinction — do not break it

`auth-token.interceptor.ts` triggers a token refresh **only** on a 401 with no body — the framework's bearer challenge. A 401 that carries a `ProblemDetails` body (that is, one with a `title`) is treated as a business error and passed through to the page.

This exists because `POST /auth/change-password` answers a wrong current password with `ResultError.Unauthorized` → 401 plus `ProblemDetails`. If that response ever became bodyless, a password typo would silently sign the user out.

`ApiControllerBase` already gets this right — it uses `StatusCode(401, CreateProblem(...))` rather than `Unauthorized()`, precisely so a body is present. Keep it that way, and never use bare `Unauthorized()` or `Forbid()` in a new controller.

---

## Frontend Contract

Nothing new is consumed. Three existing behaviours become correct:

| Change | Frontend effect |
|---|---|
| Enum converter | Prerequisite for every DTO from Phase 03 onward. Without it, `stock.0` instead of `stock.inStock`. |
| UTC timestamps | `core/utils/api-date.ts` becomes redundant and can be deleted. |
| Rate limiting restored | Matches what `manga-store\README.md` already documents: "`/auth/*` is limited to 10 requests per minute per IP". |

`CommerceOptions` values must match `CART_RULES` in `cart.model.ts` exactly — `50`, `4.99`, `7.50`, `0.14`, `10` — or the cart will display one total and the order will charge another until the frontend stops calculating locally in Phase 06.

---

## Testing

### Unit tests (`MangaStore.UnitTests`)

| Test | Asserts |
|---|---|
| `MoneyTests.Round_HalfCase_RoundsAwayFromZero` | `Money.Round(1.275m) == 1.28m` and `Money.Round(2.005m) == 2.01m`. The regression guard for the banker's-rounding trap. |
| `MoneyTests.Round_NegativeHalfCase_RoundsAwayFromZero` | `Money.Round(-1.275m) == -1.28m`. |
| `ResultErrorTests.Validation_WithEntityAndReason_QualifiesTitle` | `ResultError.Validation("Coupon", "Expired", "…").Title == "Coupon.Expired"` and `.Code == ResultErrorCodes.Validation`. |
| `ResultErrorTests.Validation_WithMessageOnly_KeepsBareTitle` | The existing single-argument overload still produces `"Validation"`. |
| `CommerceOptionsTests.Defaults_MatchFrontendCartRules` | Each default equals the documented `CART_RULES` value. Cheap, and it fails loudly if someone edits one side only. |

### Integration tests (`MangaStore.IntegrationTests`)

| Test | Asserts |
|---|---|
| `SerializationTests.Enum_SerializesAsCamelCaseString` | Add a throwaway test-double controller beside `AdminOnlyController` returning a record with an enum; assert the JSON contains `"inStock"`, not `0`. |
| `SerializationTests.DateTime_CarriesZDesignator` | `GET /api/v1/users/me` as the seeded admin; assert the raw `createdAt` string ends with `Z` and round-trips as `DateTimeKind.Utc`. **This is the test that pins the timestamp finding** — it fails today. |
| `RateLimitTests.AuthEndpoint_BeyondLimit_Returns429` | Override `RateLimit:AuthPermitLimit` to a low value for this class only, then loop `POST /auth/login` past it and assert 429. |
| `CorsTests.DisallowedOrigin_IsNotEchoed` | Preflight `OPTIONS` with `Origin: https://evil.example`; assert no `Access-Control-Allow-Origin` header. |

### Regression

All 29 existing unit tests and all 17 existing integration tests must pass unchanged. The enum and `DateTime` converters change `AuthApiTests` payloads only in ways those tests do not assert on — confirm that, do not assume it.

### Edge cases

- `Accept-Language: ar-EG,ar;q=0.9,en;q=0.8` → resolves to `ar`.
- `Accept-Language: fr` → resolves to `en` (default), not a 406.
- `Accept-Language` absent → resolves to `en`.
- `Accept-Language: *` → resolves to `en`.
- A malformed header must not throw; resolve to the default.

---

## Acceptance Criteria

- [ ] `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` registered; a test proves an enum serialises as a camelCase string.
- [ ] `UtcDateTimeConverter` and its nullable sibling registered; `GET /users/me` returns `createdAt` ending in `Z`.
- [ ] `Cors:AllowedOrigins` is an explicit list containing `http://localhost:3000`; it is not `["*"]`.
- [ ] `RateLimitOptions` restored, `fixed` and `auth` policies registered, `UseRateLimiter()` in the pipeline, `[EnableRateLimiting(RateLimitOptions.AuthPolicy)]` back on `AuthController`.
- [ ] `CommerceOptions` bound with `.ValidateDataAnnotations().ValidateOnStart()`; the `Commerce` section is in `appsettings.json`; defaults match `CART_RULES`.
- [ ] `Money.Round` exists and rounds half away from zero; unit tests cover `1.275` and `-1.275`.
- [ ] `IRequestLanguage` and `SupportedLanguages` in Application; implementation registered scoped in API; all five `Accept-Language` edge cases behave as listed.
- [ ] `ResultError.Validation(entity, reason, message)` exists and produces `Coupon.Expired`-shaped titles.
- [ ] `ConfigureConventions` applies `decimal(18,2)` and maps `DateOnly` to `date`.
- [ ] `BaseEntity.Id` uses `Guid.CreateVersion7()`.
- [ ] `.gitignore` no longer excludes `docs/backend-phases/`; `git status` shows the phase files as trackable.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds. **XML docs are mandatory** — `GenerateDocumentationFile=true` turns a missing `<summary>` into a build error.
- [ ] `dotnet ef migrations has-pending-model-changes` reports no drift, or a migration for the convention change is committed.
- [ ] `dotnet test` green: the existing 46 tests plus the new ones.
- [ ] No entity, no `DbSet`, no controller and no endpoint was added.

---

## Dependencies

```text
Depends on:
  Nothing. This is the baseline phase.

Blocks:
  Every other phase (02-16).
  Phase 03 hard-blocks on the enum converter and IRequestLanguage.
  Phases 06, 07, 08 hard-block on CommerceOptions and Money.Round.
  Phase 07 hard-blocks on ResultError.Validation(entity, reason, message).

Can be implemented independently:
  Yes - it touches no feature and adds no domain concept.
```
