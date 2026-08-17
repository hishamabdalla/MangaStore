# MangaStore

## PROJECT OVERVIEW
.NET 10 Web API built with **Clean Architecture** and a plain **service layer pattern** (no MediatR, no CQRS, no minimal APIs). Controllers are dumb. Services own all business logic and return `Result<T>`.

## ARCHITECTURE MAP

- **Domain** — Entities, value objects, domain events, repository interfaces, domain exceptions, enums. **Zero external dependencies.** Knows nothing about EF, AutoMapper, ASP.NET, or the outside world.
- **Application** — Service interfaces and implementations, DTOs (request/response), FluentValidation validators, AutoMapper profiles, application-level exceptions, `Result<T>`, pagination, `IValidationService`. References Domain only.
- **Infrastructure** — `AppDbContext`, EF Core entity configurations (Fluent API only), repository implementations, `UnitOfWork`, soft-delete & audit interceptors, mockable clock, external service clients. References Application + Domain.
- **API** — Controllers only. Middleware, filters, `Program.cs`, `appsettings.json`, DI wiring. References Application + Infrastructure.

**Dependency rule:** API → Application → Domain. Infrastructure → Application → Domain. **Domain depends on nothing.**

> When in doubt, put it in Application, not Domain.

## THE CONTROLLER RULE (read this twice)
A controller method may do **exactly three things**:
1. Call one service method
2. Pass the `Result` to `HandleResult()` (or `HandleCreated()` / `HandleDelete()`)
3. Return

> More than ~3 lines in a controller method is an architecture violation.

No mapping. No validation. No `if/else`. No `_mapper.Map<>`. No repository calls. No business decisions.

## HOW THE SERVICE LAYER WORKS
Chain: **Controller → IXxxService → IXxxRepository → AppDbContext**

Each service method:
1. Validates the request via `await _validationService.ValidateAsync(request, ct)` — returns early on failure.
2. Executes business logic.
3. Persists via `_unitOfWork.SaveChangesAsync(ct)`.
4. Returns `Result<T>` — **never throws**.

Domain exceptions are thrown only from **Infrastructure** and caught by the global exception middleware.

## HOW AUTOMAPPER IS USED
- Profiles live in `Application/Features/Xxx/Profiles/XxxProfile.cs`.
- `_mapper.Map<>` is called **only inside service classes** — never in controllers, repos, or middleware.

## HOW IValidationService WORKS
- Every service calls `_validationService.ValidateAsync(request, ct)` first.
- Returns `Result` — service short-circuits with `Result.Fail(...)` on validation failure.

## ADDING A NEW FEATURE — EXACT CHECKLIST
1.  Domain: entity inheriting `BaseEntity`
2.  Domain: domain events (if needed)
3.  Domain: `IXxxRepository` extending `IRepository<T>`
4.  Application: `Features/Xxx/` folder
5.  Application: DTOs, Validators, AutoMapper Profile
6.  Application: `IXxxService` + `XxxService` returning `Result<T>`
7.  Infrastructure: `XxxConfiguration` (`IEntityTypeConfiguration<T>`)
8.  Infrastructure: `XxxRepository` + `DbSet<Xxx>` in `AppDbContext`
9.  Infrastructure: EF migration (`dotnet ef migrations add`)
10. API: `XxxController` inheriting `ApiControllerBase`
11. Scrutor auto-discovers — **no manual DI registration needed**
    - Application scans `Features.*` types named `*Service`.
    - Infrastructure scans types named `*Repository`, `*Service`, or `*Sender`.

## AUTHENTICATION

Sessions are a short-lived JWT access token plus a rotating refresh token. Endpoints live on `AuthController` (`/api/v1/auth/*`) and `UsersController` (`/api/v1/users/me`).

### The Identity seam
ASP.NET Core Identity is an EF-flavoured framework, so it is **confined to Infrastructure**:

- `Infrastructure/Identity/ApplicationUser` — the persisted account. Never referenced above Infrastructure.
- `Application/Common/Identity/IIdentityService` — the only way Application touches user accounts. Returns `AppUserInfo`, `Result`, and primitives; never an Identity type.
- `Application/Common/Identity/ICurrentUser` — implemented in API, the only layer that knows `HttpContext`.
- `Application/Common/Identity/ITokenService` — implemented by `JwtTokenService`.

> Need a user operation from a service? Add a method to `IIdentityService`. Don't inject `UserManager`.

### Rules that are easy to break
- Access tokens use **short claim names** (`sub`, `role`, `email`). The bearer handler sets `MapInboundClaims = false` and `RoleClaimType = "role"` to match — change one and `[Authorize(Roles = ...)]` stops matching *silently*.
- Only the **SHA-256 hash** of a refresh token is stored. The raw value is returned once.
- Refresh tokens are **single-use**. Presenting a revoked one is treated as theft and revokes every session for that user.
- Sign-in failures must be **indistinguishable** — a wrong password and an unknown email return the same status and message. `/forgot-password` always returns 204.
- Changing or resetting a password revokes every existing session.
- There is **no email confirmation**; registration returns a usable session immediately.

## RESTFUL API DESIGN

### Resource-Oriented URLs
URLs identify resources (nouns), not actions. Controller names are **plural nouns**.

| Operation         | Method   | URL                     |
|-------------------|----------|-------------------------|
| List all          | `GET`    | `/api/v1/products`      |
| Get one           | `GET`    | `/api/v1/products/{id}` |
| Create            | `POST`   | `/api/v1/products`      |
| Full update       | `PUT`    | `/api/v1/products/{id}` |
| Partial update    | `PATCH`  | `/api/v1/products/{id}` |
| Delete            | `DELETE` | `/api/v1/products/{id}` |

### HTTP Method Semantics
- `GET` — safe and idempotent; never mutates state.
- `POST` — creates a resource; not idempotent.
- `PUT` — full replacement; idempotent.
- `PATCH` — partial update; idempotent.
- `DELETE` — removes a resource; idempotent.

### HTTP Status Code Conventions
| Status | When to use                                                      |
|--------|------------------------------------------------------------------|
| 200    | Successful GET / PUT / PATCH                                     |
| 201    | Resource created (POST); include `Location` header via route     |
| 204    | Successful DELETE — no body                                      |
| 400    | Malformed request / general client error                         |
| 401    | Not authenticated, or credentials rejected (bad password, dead refresh token) |
| 403    | Authenticated but not authorised — return `ProblemDetails` body  |
| 404    | Resource not found                                               |
| 409    | Conflict (duplicate, version mismatch, etc.)                     |
| 422    | Validation failure (well-formed request but semantically invalid)|
| 500    | Unhandled server error — never expose internal details           |

### How Status Codes Map to `ResultError`
`ApiControllerBase.HandleResult` maps error codes automatically:

| `ResultError` factory   | HTTP status |
|-------------------------|-------------|
| `ResultError.NotFound`     | 404         |
| `ResultError.Conflict`     | 409         |
| `ResultError.Unauthorized` | 401         |
| `ResultError.Forbidden`    | 403         |
| `ResultError.Validation`   | 422         |
| `ResultError.Failure`      | 400         |

### `[ProducesResponseType]` Requirements
Every controller action **must** declare its possible status codes using the generic `[ProducesResponseType<T>]` form (available from .NET 7+).
Common statuses shared by all endpoints (401, 500) are declared once on `ApiControllerBase`.

**Type rules:**

| Response category | Type argument |
|-------------------|---------------|
| 200 single resource | `ProductDto` (or the relevant DTO) |
| 200 / 201 list | `PaginatedList<ProductDto>` (or the relevant DTO) |
| 201 created | The created resource DTO |
| 204 no content | *(no type — use bare `[ProducesResponseType(204)]`)* |
| 400 / 401 / 403 / 404 / 409 / 422 / 500 | `ProblemDetails` |

**Example:**
```csharp
[ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
```

## XML DOCUMENTATION

Every public type and member **must** have XML docs. Use the following conventions:

### Tags to use
| Tag | When |
|-----|------|
| `<summary>` | Every public type and member — one line max |
| `<param>` | Every parameter whose purpose isn't obvious from the name |
| `<returns>` | When the return value needs clarification beyond the type |
| `<exception>` | When a member can throw (e.g. accessing `Result<T>.Value` on a failure) |
| `<inheritdoc/>` | On concrete implementations — avoids duplicating interface docs |
| `<inheritdoc cref="IXxx"/>` | On the implementing class declaration itself |
| `<see langword="null"/>` | When referencing `null` inline |
| `<see cref="TypeOrMember"/>` | When referencing another type or member inline |

### Rules
- One `<summary>` line only — never multi-line or multi-sentence.
- Implementations use `/// <inheritdoc/>` on each method; write the full doc on the interface.
- Positional record parameters use `<param>` tags on the record declaration, not on each property.
- Don't state what the code does if the name already says it — only add a doc when it adds meaning.

### Examples
```csharp
// Interface — full docs here
/// <summary>Returns the entity with the given <paramref name="id"/>, or <see langword="null"/> if not found.</summary>
Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

// Implementation — inherit, don't repeat
/// <inheritdoc/>
public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) { ... }

// Record — params on the declaration
/// <summary>Read model returned by product endpoints.</summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="UpdatedAt">UTC timestamp of last update, or <see langword="null"/> if never updated.</param>
public sealed record ProductDto(Guid Id, ..., DateTime? UpdatedAt);

// Exception property worth documenting
/// <summary>Gets the value produced by a successful operation.</summary>
/// <exception cref="InvalidOperationException">Thrown when accessed on a failed result.</exception>
public TValue Value => ...;
```

## NAMING CONVENTIONS
| Kind         | Convention                                                            |
|--------------|------------------------------------------------------------------------|
| Services     | `IProductService` / `ProductService`                                   |
| Repositories | `IProductRepository` / `ProductRepository`                             |
| DTOs         | `ProductDto` / `CreateProductRequest` / `UpdateProductRequest`         |
| Validators   | `CreateProductRequestValidator`                                        |
| Profiles     | `ProductProfile`                                                       |
| Controllers  | `ProductsController` (plural)                                          |

## CODE RULES
- Services return `Result<T>` — **never throw**.
- Domain exceptions thrown only from Infrastructure.
- Controllers only call services.
- No EF Core in Application or Domain.
- No manual mapping — AutoMapper profiles only.
- Every request DTO has a validator.
- No data annotations on Domain entities — Fluent API only.
- `CancellationToken` in every async signature.
- Use C# 14 features where they improve clarity.

## DO NOT DO THESE THINGS
- Don't add MediatR or any CQRS library.
- Don't add Minimal API endpoints.
- Don't write logic inside controllers.
- Don't call `_mapper.Map<>` in a controller.
- Don't call repositories from controllers.
- Don't put EF annotations on Domain entities.
- Don't reference Infrastructure from Domain or Application.
- Don't register services manually if Scrutor can scan them.
- Don't use `Forbid()` for 403 responses — it triggers an auth challenge with no body. Use `StatusCode(403, CreateProblem(...))` via `HandleResult`. (Framework-level 403s from `[Authorize]` are handled by `ProblemDetailsAuthorizationResultHandler`.)
- Don't put `[AllowAnonymous]` on a controller that also has `[Authorize]` actions — the class-level attribute wins and silently opens them. Apply it per action.
- Don't use bare `[ProducesResponseType(statusCode)]` when a type exists — always use `[ProducesResponseType<T>(statusCode)]`. Exception: 204 No Content has no body.

## TESTING STRATEGY
- **Unit tests** (`MangaStore.UnitTests`) — substitute collaborators with NSubstitute, test services directly, assert with **Shouldly**.
- **Integration tests** (`MangaStore.IntegrationTests`) — `WebApplicationFactory<Program>` + SQLite in-memory.
  - The factory creates the schema **before the host starts**, because start-up seeds roles and would otherwise query tables that don't exist yet.
  - One database per test class, so tests must use distinct email addresses.

> The `Product*` names in the tables above are illustrative only — the sample feature was deleted. The catalogue domain has not been designed yet.
