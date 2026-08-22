# MangaStore — Architecture & Feature Development Guide

---

## Table of Contents

1. [Big Picture — What Is This Project?](#1-big-picture)
2. [Dependency Rule — Who Can Talk to Whom?](#2-dependency-rule)
3. [Layer-by-Layer Breakdown](#3-layer-by-layer-breakdown)
   - [Domain](#31-domain-layer)
   - [Application](#32-application-layer)
   - [Infrastructure](#33-infrastructure-layer)
   - [API](#34-api-layer)
4. [How a Request Flows End-to-End](#4-how-a-request-flows-end-to-end)
5. [Key Patterns Explained](#5-key-patterns-explained)
   - [Result\<T\> — Railway-Oriented Programming](#51-resultt--railway-oriented-programming)
   - [Soft Delete](#52-soft-delete)
   - [Audit Timestamps](#53-audit-timestamps)
   - [Domain Events](#54-domain-events)
   - [Pagination](#55-pagination)
   - [Validation](#56-validation)
6. [Building a New Feature — Step-by-Step](#6-building-a-new-feature--step-by-step)
7. [The Auth Feature — Worked Example](#7-the-auth-feature--worked-example)
8. [Testing Strategy](#8-testing-strategy)
9. [Configuration & Secrets](#9-configuration--secrets)
10. [Common Mistakes to Avoid](#10-common-mistakes-to-avoid)

---

## 1. Big Picture

This is a **.NET 10 Web API** built with **Clean Architecture**. It is designed as a reusable starter template — clone it, rename namespaces, and start adding features on day one without fighting architecture debt.

**Core design choices:**
- **No MediatR / CQRS** — plain service layer (`IXxxService → XxxService`)
- **No Minimal APIs** — traditional controllers only
- **Railway-oriented error handling** — services return `Result<T>`, never throw
- **EF Core** with interceptors for audit + soft-delete, no data annotations on entities
- **Scrutor** for convention-based DI — no manual service registration
- **FluentValidation** for all request validation
- **AutoMapper** for DTO mapping (only inside services)
- **Serilog** for structured logging

---

## 2. Dependency Rule

```
┌─────────────────────────────────────┐
│              API Layer              │  ← Controllers, Middleware, Program.cs
│   References: Application + Infra  │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│         Infrastructure Layer        │  ← EF Core, Repositories, Interceptors
│        References: Application      │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│          Application Layer          │  ← Services, DTOs, Validators, Profiles
│           References: Domain        │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│            Domain Layer             │  ← Entities, Events, Interfaces
│         References: NOTHING         │
└─────────────────────────────────────┘
```

**Golden Rule:** Inner layers never reference outer layers. Domain knows nothing about EF Core, ASP.NET, or AutoMapper. Application knows nothing about SQL Server or HTTP.

---

## 3. Layer-by-Layer Breakdown

### 3.1 Domain Layer

**Location:** `src/MangaStore.Domain/`

This is the heart of the application. Zero external dependencies — no NuGet packages, no framework references.

#### `Common/BaseEntity.cs`
Every entity in the system inherits from this.

```
BaseEntity
├── Id            : Guid          (auto-generated, protected setter)
├── CreatedAt     : DateTime      (set by AuditInterceptor on insert)
├── UpdatedAt     : DateTime?     (set by AuditInterceptor on update)
├── IsDeleted     : bool          (set by SoftDeleteInterceptor, internal setter)
└── DomainEvents  : IReadOnlyCollection<IDomainEvent>
```

You never set `CreatedAt`, `UpdatedAt`, or `IsDeleted` manually. Infrastructure handles all of that automatically.

#### `Common/IAuditableEntity.cs`
Interface marking that `AuditInterceptor` should stamp timestamps. `BaseEntity` implements it.

#### `Events/DomainEvent.cs`
Base class for all domain events. Auto-sets:
- `EventId` — unique GUID per event instance
- `OccurredOn` — UTC timestamp when the event was created

#### `Interfaces/IRepository<T>`
The generic contract every repository must implement:

| Method | What it does |
|--------|-------------|
| `GetByIdAsync(id)` | Returns tracked entity (for mutations) or null |
| `GetAllAsync()` | All entities (no-tracking) |
| `GetPagedAsync(skip, take)` | Paginated, ordered by `CreatedAt` descending |
| `CountAsync()` | Total count (respects soft-delete filter) |
| `AddAsync(entity)` | Adds to change tracker |
| `Delete(entity)` | Marks for deletion (interceptor converts to soft-delete) |
| `ExistsAsync(id)` | Boolean check |

> `Update()` was intentionally removed. If you load an entity via `GetByIdAsync`, EF Core tracks it. Mutating it and calling `SaveChanges` is enough — no `Update()` needed.

**When `IRepository<T>` isn't enough — add a method, not a framework.**

The catalogue query filters on search text, category, price range, product kind, stock and sale state, then sorts five ways and pages. `IRepository<T>` cannot express that, and it should not try. The decision taken is a **bespoke method on the feature repository**:

```csharp
Task<PaginatedList<Product>> SearchAsync(ProductQuery query, string languageCode, CancellationToken ct = default);
```

- **No specification pattern.** It moves query construction into Application as data, which then needs its own combinators, its own tests, and its own escape hatch the first time a query needs `AsSplitQuery` or a translation join. The cost lands before the benefit does.
- **A repository never returns `IQueryable<T>`.** That leaks EF into Application, makes the boundary decorative, and defers query failures to enumeration time — inside a service that is supposed to return `Result<T>` rather than throw.
- One method per real query shape. If two features need the same shape, they share the method; if they need different shapes, they get different methods.

#### `Interfaces/IUnitOfWork`
Single method: `SaveChangesAsync()`. Wraps EF Core's `SaveChangesAsync`, collects + dispatches domain events after the commit.

#### `Interfaces/IDateTime`
Mockable clock abstraction. One property: `UtcNow`. Never use `DateTime.UtcNow` directly in domain or application code — use `IDateTime` so tests can control time.

#### `Exceptions/`
Domain exceptions that map directly to HTTP status codes:

| Exception | HTTP Status |
|-----------|------------|
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
| `ForbiddenException` | 403 |

These are **only thrown from Infrastructure** (not from services). Services return `ResultError` instead.

---

### 3.2 Application Layer

**Location:** `src/MangaStore.Application/`

Owns all business logic. References only the Domain layer.

#### `Common/Result/Result<T>`
The return type of every service method. A result is either:
- **Success** — carries a `TValue`
- **Failure** — carries a `ResultError` (code + message)

```csharp
// Creating results
Result<ProductDto> success = dto;                          // implicit conversion
Result<ProductDto> failure = ResultError.NotFound("...");  // implicit conversion
Result failure2 = Result.Fail(ResultError.Validation("Name is required"));

// Consuming results
if (result.IsSuccess)
    return result.Value;

// Or use Match (functional style)
return result.Match(
    onSuccess: dto => Ok(dto),
    onFailure: err => BadRequest(err.Message)
);
```

#### `Common/Result/ResultError`
Describes WHY something failed:

| Factory | Code | Typical use |
|---------|------|-------------|
| `ResultError.NotFound(msg)` | `"NotFound"` | Entity not found |
| `ResultError.Conflict(msg)` | `"Conflict"` | Duplicate, version clash |
| `ResultError.Unauthorized(msg)` | `"Unauthorized"` | Credentials missing, wrong, or expired → 401 |
| `ResultError.Forbidden(msg)` | `"Forbidden"` | Authenticated but not permitted → 403 |
| `ResultError.Validation(msg)` | `"Validation"` | FluentValidation failure |
| `ResultError.Failure(msg)` | `"Failure"` | Generic business rule violation |

#### `Common/Pagination/PaginationParams`
Bound from query string via `[FromQuery]`. Clamps values automatically:
- `PageNumber` — minimum 1
- `PageSize` — between 1 and 100

#### `Common/Pagination/PaginatedList<T>`
Returned by list endpoints:

```json
{
  "items": [...],
  "totalCount": 42,
  "pageNumber": 1,
  "pageSize": 10,
  "totalPages": 5,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

#### `Common/Validation/IValidationService`
Every service calls this before doing any work:

```csharp
var validation = await _validationService.ValidateAsync(request, ct);
if (validation.IsFailure)
    return validation.Error;  // short-circuit with 422
```

It resolves the matching `IValidator<TRequest>` from DI and returns a `Result`. If no validator is registered, it logs a warning and returns success (so you don't accidentally block unvalidated requests silently — the warning in logs is your signal).

#### `Common/Events/IDomainEventListener<TEvent>`
Implement this to react to domain events:

```csharp
public sealed class SendWelcomeEmailOnProductCreated
    : IDomainEventListener<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent evt, CancellationToken ct)
    {
        // send email, call external API, etc.
    }
}
```

Scrutor auto-registers all listeners — no manual DI needed.

#### `Features/Xxx/` — The Feature Folder Structure

Each feature lives in its own folder:

```
Features/
└── Products/
    ├── IProductService.cs          ← interface
    ├── ProductService.cs           ← implementation
    ├── Dtos/
    │   ├── ProductDto.cs           ← read model (returned to client)
    │   ├── CreateProductRequest.cs ← create payload
    │   └── UpdateProductRequest.cs ← update payload
    ├── Validators/
    │   ├── ProductRequestValidatorBase.cs   ← shared rules
    │   ├── CreateProductRequestValidator.cs ← create-specific rules
    │   └── UpdateProductRequestValidator.cs ← update-specific rules
    └── Profiles/
        └── ProductProfile.cs       ← AutoMapper mappings
```

#### Service Method Pattern

Every service method follows this exact pattern:

```
1. Validate request  → return early if invalid (422)
2. Business logic    → fetch, transform, create entities
3. Persist           → _unitOfWork.SaveChangesAsync()
4. Return Result<T>  → map entity to DTO and return
```

---

### 3.3 Infrastructure Layer

**Location:** `src/MangaStore.Infrastructure/`

Implements everything that touches external systems: database, file system, clocks, external APIs.

#### `Persistence/AppDbContext.cs`
The EF Core `DbContext`. Key behaviors:
- Default tracking: `NoTrackingWithIdentityResolution` — reads don't track by default (performance)
- `GetByIdAsync` explicitly uses `AsTracking()` — entities loaded for mutation ARE tracked
- `OnModelCreating` loads all `IEntityTypeConfiguration<T>` from the assembly automatically
- `ConfigureConventions` sets `decimal` to `(18,2)` and `DateOnly` to the `date` column type, so a new money column cannot silently inherit the provider's default precision and truncate

> **The no-tracking trap.** Tracking is off globally. A query whose result you intend to mutate and save **must** call `.AsTracking()`, or `SaveChangesAsync` will report success and write nothing. This fails silently — no exception, no log, just a persisted value that never changed.
>
> For a contended counter (stock, usage limits) prefer a guarded `ExecuteUpdateAsync` over load-mutate-save entirely. Note it executes **immediately** rather than at `SaveChangesAsync`, so inside a transaction it must be sequenced deliberately, and it bypasses the interceptors — meaning `UpdatedAt` has to be set in the `SetProperty` chain by hand.

#### `Persistence/Configurations/XxxConfiguration.cs`
All entity mapping is done here via Fluent API. **No data annotations on entities.**

Example things you configure here:
- Column names, max lengths, required fields
- `HasQueryFilter(e => !e.IsDeleted)` — soft-delete filter (automatic, invisible to queries)
- Indexes
- Relationships

#### `Persistence/Repositories/GenericRepository<T>`
Default implementation of `IRepository<T>`. All reads use `AsNoTracking()` for performance except `GetByIdAsync` which uses `AsTracking()` for mutation scenarios.

#### `Persistence/Repositories/XxxRepository.cs`
Inherits `GenericRepository<T>` and adds feature-specific queries. If you don't need custom queries, the class can be empty — it still exists so Scrutor registers it as `IXxxRepository`.

#### `Persistence/UnitOfWork.cs`

```
SaveChangesAsync():
  1. Collect all domain events from tracked entities
  2. Clear domain events from entities (so they can't be dispatched twice)
  3. await _context.SaveChangesAsync()   ← commit to DB
  4. await _dispatcher.DispatchAsync()   ← fire events AFTER commit
  5. Errors in listeners are logged but do NOT roll back the DB write
     → listeners must be IDEMPOTENT
```

#### `Persistence/Interceptors/AuditInterceptor.cs`
Runs before every `SaveChanges`:
- `EntityState.Added` → sets `CreatedAt = now`
- `EntityState.Modified` → sets `UpdatedAt = now`

#### `Persistence/Interceptors/SoftDeleteInterceptor.cs`
Runs before every `SaveChanges`:
- `EntityState.Deleted` → sets `IsDeleted = true`, `UpdatedAt = now`, changes state to `Modified`

This is why calling `_repo.Delete(entity)` results in an UPDATE, not a DELETE in the database.

#### `Persistence/DomainEventDispatcher.cs`
Resolves all `IDomainEventListener<TEvent>` from DI and calls `HandleAsync` on each. Uses a `ConcurrentDictionary` cache of reflection-based invokers so the reflection cost is paid only once per event type. Each listener is wrapped in its own `try/catch` so one failing listener never skips the rest.

#### `Services/SystemDateTime.cs`
Wraps `TimeProvider.System.GetUtcNow()`. In tests you can inject `FakeTimeProvider` to control time.

---

### 3.4 API Layer

**Location:** `src/MangaStore.API/`

Thin layer. Controllers are dumb — they call one service method and pass the result to a helper.

#### `Controllers/Base/ApiControllerBase.cs`
Three helper methods every controller uses:

| Method | Success response | Failure response |
|--------|-----------------|-----------------|
| `HandleResult(result)` | `200 OK` with body | 4xx via `MapErrorToResponse` |
| `HandleCreated(result, actionName, routeValues)` | `201 Created` with `Location` header | 4xx via `MapErrorToResponse` |
| `HandleDelete(result)` | `204 No Content` | 4xx via `MapErrorToResponse` |

`MapErrorToResponse` maps `ResultError.Code` → HTTP status:

```
"NotFound"   → 404
"Conflict"   → 409
"Forbidden"  → 403
"Validation" → 422
"Failure"    → 400
```

#### `Controllers/XxxController.cs`
**Exactly 3 lines per action:**
1. Call one service method
2. Pass result to `HandleResult` / `HandleCreated` / `HandleDelete`
3. Return

```csharp
[HttpGet("{id:guid}")]
public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct) =>
    HandleResult(await _productService.GetByIdAsync(id, ct));
```

No mapping. No validation. No if/else. No business logic.

#### `Infrastructure/GlobalExceptionHandler.cs`
Catches any unhandled exception (from Infrastructure) and returns a proper `ProblemDetails` response:
- `NotFoundException` → 404
- `ConflictException` → 409
- `ForbiddenException` → 403
- Anything else → 500 (detail hidden in non-Development environments)

#### `Options/`
Strongly-typed configuration classes with `ValidateOnStart()` — the app **fails to start** if required config is missing:

| Class | Config section | Required fields |
|-------|---------------|----------------|
| `JwtOptions` | `Jwt` | `Issuer`, `Audience`, `Secret` (min 32 chars) |
| `CorsOptions` | `Cors` | `AllowedOrigins` (min 1) |
| `RateLimitOptions` | `RateLimit` | none (has defaults) |
| `AppOptions` | `App` | `FrontendBaseUrl` |

#### `Program.cs` — Middleware Pipeline Order

```
UseExceptionHandler         ← must be first to catch everything below
UseHsts                     ← (non-Development only)
UseHttpsRedirection
UseSerilogRequestLogging    ← logs every HTTP request
UseCors
UseRateLimiter
UseResponseCompression
UseAuthentication
UseAuthorization
MapControllers
MapHealthChecks("/health")
```

---

## 4. How a Request Flows End-to-End

**Example: `POST /api/v1/products`**

```
Client
  │
  ▼
ExceptionHandlingMiddleware   (catches anything that escapes below)
  │
  ▼
RateLimiterMiddleware         (429 if limit exceeded)
  │
  ▼
AuthenticationMiddleware      (validates JWT, sets ClaimsPrincipal)
  │
  ▼
AuthorizationMiddleware       (checks [Authorize] policies)
  │
  ▼
ProductsController.CreateAsync(CreateProductRequest request)
  │  ← controller reads request body, passes to service
  ▼
ProductService.CreateAsync(request, ct)
  │
  ├─ 1. _validationService.ValidateAsync(request)
  │       └─ CreateProductRequestValidator runs FluentValidation rules
  │          if invalid → return ResultError.Validation(...)  ──► 422
  │
  ├─ 2. Product.Create(name, description, price)
  │       └─ creates Product entity
  │          raises ProductCreatedEvent
  │
  ├─ 3. _productRepository.AddAsync(product)
  │       └─ marks entity as Added in EF change tracker
  │
  ├─ 4. _unitOfWork.SaveChangesAsync()
  │       │
  │       ├─ AuditInterceptor  → sets product.CreatedAt = now
  │       ├─ EF Core           → INSERT INTO Products (...)
  │       └─ DomainEventDispatcher
  │               └─ calls HandleAsync on all IDomainEventListener<ProductCreatedEvent>
  │
  └─ 5. return _mapper.Map<ProductDto>(product)  ──► Result<ProductDto>
  │
  ▼
HandleCreated(result, nameof(GetByIdAsync), new { id = result.Value.Id })
  │
  ▼
201 Created
Location: /api/v1/products/{newId}
Body: { "id": "...", "name": "...", ... }
```

---

## 5. Key Patterns Explained

### 5.1 `Result<T>` — Railway-Oriented Programming

Instead of throwing exceptions for expected failures, services return `Result<T>`. Think of it as two parallel tracks — success track and failure track.

```
Request ──► Validate ──► Business Logic ──► Persist ──► Map ──► 201
               │                │                          
               └── Fail ──────────────────────────────────► 422 / 404 / 409
```

**Consuming a result in a controller:**
```csharp
// Simple GET
return HandleResult(await _service.GetByIdAsync(id, ct));

// POST with Location header
var result = await _service.CreateAsync(request, ct);
return HandleCreated(result, nameof(GetByIdAsync), new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
```

### 5.2 Soft Delete

Entities are **never physically deleted**. When you call `_repo.Delete(entity)`:
1. EF Core marks entity as `Deleted`
2. `SoftDeleteInterceptor` intercepts before save
3. Sets `IsDeleted = true` and `UpdatedAt = now`
4. Changes state back to `Modified`
5. SQL: `UPDATE Products SET IsDeleted = 1, UpdatedAt = ... WHERE Id = ...`

The `HasQueryFilter(p => !p.IsDeleted)` in `ProductConfiguration` makes deleted entities invisible to all queries automatically. You never need to add `WHERE IsDeleted = 0` anywhere.

To see deleted entities (admin, auditing):
```csharp
db.Products.IgnoreQueryFilters().Where(...);
```

### 5.3 Audit Timestamps

`CreatedAt` and `UpdatedAt` are managed entirely by `AuditInterceptor`. You never set them manually. When you create an entity, `CreatedAt` is set. Every update sets `UpdatedAt`.

### 5.4 Domain Events

Domain events let you react to things that happen in the domain **without coupling** the domain to the reaction.

**Flow:**
```
1. Entity raises event during business operation
   └─ product.AddDomainEvent(new ProductCreatedEvent(id, name))

2. UnitOfWork collects events before SaveChanges
3. DB is committed (event cleared from entity)
4. DomainEventDispatcher calls all IDomainEventListener<ProductCreatedEvent>
```

**Important:** Listeners run **after** the DB commit. If a listener fails, the DB write already happened. Listeners **must be idempotent** (safe to run more than once).

To react to an event:
```csharp
public sealed class NotifyInventoryOnProductCreated
    : IDomainEventListener<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent evt, CancellationToken ct)
    {
        // do side-effect work here
    }
}
// No DI registration needed — Scrutor finds it automatically
```

### 5.5 Pagination

Always use `PaginationParams` for list endpoints:

```csharp
[HttpGet]
public async Task<IActionResult> GetAllAsync([FromQuery] PaginationParams pagination, CancellationToken ct) =>
    HandleResult(await _service.GetAllAsync(pagination, ct));
```

Query string: `GET /api/v1/products?pageNumber=2&pageSize=20`

Response includes `totalCount`, `totalPages`, `hasNextPage`, etc.

### 5.6 Validation

Every request DTO has a validator. Validators inherit from `AbstractValidator<T>`. Common validators for a single feature share a `XxxRequestValidatorBase`.

```csharp
public sealed class ProductRequestValidatorBase<TRequest> : AbstractValidator<TRequest>
    where TRequest : IProductRequest
{
    protected ProductRequestValidatorBase()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0);
    }
}
```

Validators are auto-registered by Scrutor — no manual DI.

---

## 6. Building a New Feature — Step-by-Step

Say you need to add an **Orders** feature. Here is the exact sequence:

### Step 1 — Domain: Entity

```
src/MangaStore.Domain/Features/Orders/Order.cs
```

```csharp
public sealed class Order : BaseEntity
{
    public string CustomerName { get; private set; } = string.Empty;
    public decimal Total { get; private set; }

    private Order() { }  // EF Core needs this

    public static Order Create(string customerName, decimal total)
    {
        var order = new Order { CustomerName = customerName, Total = total };
        order.AddDomainEvent(new OrderCreatedEvent(order.Id, customerName));
        return order;
    }

    public void UpdateCustomerName(string name) => CustomerName = name;
}
```

### Step 2 — Domain: Event (if needed)

```
src/MangaStore.Domain/Features/Orders/OrderCreatedEvent.cs
```

```csharp
public sealed class OrderCreatedEvent(Guid OrderId, string CustomerName) : DomainEvent
{
    public Guid OrderId { get; } = OrderId;
    public string CustomerName { get; } = CustomerName;
}
```

### Step 3 — Domain: Repository Interface

```
src/MangaStore.Domain/Interfaces/IOrderRepository.cs
```

```csharp
public interface IOrderRepository : IRepository<Order>
{
    // Add only feature-specific queries here
    // Task<IReadOnlyList<Order>> GetByCustomerAsync(string customer, CancellationToken ct);
}
```

### Step 4 — Application: Feature Folder

Create the folder structure:

```
src/MangaStore.Application/Features/Orders/
├── IOrderService.cs
├── OrderService.cs
├── Dtos/
│   ├── OrderDto.cs
│   ├── CreateOrderRequest.cs
│   └── UpdateOrderRequest.cs
├── Validators/
│   ├── OrderRequestValidatorBase.cs
│   ├── CreateOrderRequestValidator.cs
│   └── UpdateOrderRequestValidator.cs
└── Profiles/
    └── OrderProfile.cs
```

**`OrderDto.cs`** — read model returned to the client:
```csharp
/// <summary>Read model returned by order endpoints.</summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="CustomerName">Name of the customer who placed the order.</param>
/// <param name="Total">Order total in the store currency.</param>
/// <param name="CreatedAt">UTC timestamp of creation.</param>
/// <param name="UpdatedAt">UTC timestamp of last update, or <see langword="null"/> if never updated.</param>
public sealed record OrderDto(Guid Id, string CustomerName, decimal Total, DateTime CreatedAt, DateTime? UpdatedAt);
```

**`CreateOrderRequest.cs`**:
```csharp
public sealed record CreateOrderRequest(string CustomerName, decimal Total);
```

**`OrderRequestValidatorBase.cs`**:
```csharp
public abstract class OrderRequestValidatorBase<TRequest> : AbstractValidator<TRequest>
    where TRequest : CreateOrderRequest
{
    protected OrderRequestValidatorBase()
    {
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Total).GreaterThan(0);
    }
}
```

**`CreateOrderRequestValidator.cs`**:
```csharp
/// <summary>Validates <see cref="CreateOrderRequest"/> payloads.</summary>
public sealed class CreateOrderRequestValidator : OrderRequestValidatorBase<CreateOrderRequest> { }
```

**`OrderProfile.cs`**:
```csharp
public sealed class OrderProfile : Profile
{
    /// <summary>Initialises a new instance of <see cref="OrderProfile"/>.</summary>
    public OrderProfile() => CreateMap<Order, OrderDto>();
}
```

**`IOrderService.cs`**:
```csharp
public interface IOrderService
{
    Task<Result<OrderDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<PaginatedList<OrderDto>>> GetAllAsync(PaginationParams pagination, CancellationToken ct = default);
    Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
```

**`OrderService.cs`** — follow the same pattern as `ProductService`:
```csharp
public sealed partial class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IValidationService _validationService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(/* inject the above */) { ... }

    public async Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure) return validation.Error;

        var order = Order.Create(request.CustomerName, request.Total);
        await _orderRepository.AddAsync(order, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return _mapper.Map<OrderDto>(order);
    }
    // ... other methods
}
```

### Step 5 — Infrastructure: EF Configuration

```
src/MangaStore.Infrastructure/Persistence/Configurations/OrderConfiguration.cs
```

```csharp
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.CustomerName)
               .IsRequired()
               .HasMaxLength(300);

        builder.Property(o => o.Total)
               .HasColumnType("decimal(18,2)");

        builder.HasQueryFilter(o => !o.IsDeleted);  // ← REQUIRED for soft-delete

        builder.HasIndex(o => o.CustomerName);
    }
}
```

### Step 6 — Infrastructure: Repository

```
src/MangaStore.Infrastructure/Persistence/Repositories/OrderRepository.cs
```

```csharp
/// <inheritdoc cref="IOrderRepository"/>
public sealed class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    /// <summary>Initialises a new instance of <see cref="OrderRepository"/>.</summary>
    public OrderRepository(AppDbContext context) : base(context) { }

    // Add custom queries here if needed
}
```

### Step 7 — Infrastructure: Add DbSet

In `AppDbContext.cs`:
```csharp
/// <summary>Gets the orders table.</summary>
public DbSet<Order> Orders => Set<Order>();
```

### Step 8 — Infrastructure: EF Migration

```bash
dotnet ef migrations add AddOrders --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API
dotnet ef database update --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API
```

### Step 9 — API: Controller

```
src/MangaStore.API/Controllers/OrdersController.cs
```

```csharp
/// <summary>Manages order resources.</summary>
public sealed class OrdersController : ApiControllerBase
{
    private readonly IOrderService _orderService;

    /// <summary>Initialises a new instance of <see cref="OrdersController"/>.</summary>
    public OrdersController(IOrderService orderService) => _orderService = orderService;

    /// <summary>Returns a paginated list of all orders.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync([FromQuery] PaginationParams pagination, CancellationToken ct) =>
        HandleResult(await _orderService.GetAllAsync(pagination, ct));

    /// <summary>Returns a single order by its identifier.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct) =>
        HandleResult(await _orderService.GetByIdAsync(id, ct));

    /// <summary>Creates a new order and returns it with a <c>Location</c> header.</summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var result = await _orderService.CreateAsync(request, ct);
        return HandleCreated(result, nameof(GetByIdAsync), new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
    }

    /// <summary>Soft-deletes an order.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct) =>
        HandleDelete(await _orderService.DeleteAsync(id, ct));
}
```

### Step 10 — No DI Registration Needed

Scrutor auto-discovers:
- `OrderService` → registered as `IOrderService` (scoped)
- `OrderRepository` → registered as `IOrderRepository` (scoped)
- `CreateOrderRequestValidator` → registered as `IValidator<CreateOrderRequest>` (scoped)
- Any `IDomainEventListener<OrderCreatedEvent>` implementations

The name suffix is what makes a type discoverable:

| Assembly | Scanned suffixes |
|----------|------------------|
| Application | `*Service`, under the `Features` namespace only |
| Infrastructure | `*Repository`, `*Service`, `*Sender` |

Anything outside those conventions still needs an explicit registration — `IdentitySeeder` and
`SystemDateTime` are the current examples.

---

## 7. The Auth Feature — Worked Example

> The old `Products` sample was deleted when this became a real product. `Product*` names still
> appearing in earlier sections are illustrative only — no such code exists. `Auth` is now the
> reference implementation. Study it before building anything new.

| File | What to learn from it |
|------|----------------------|
| `Domain/Features/Auth/RefreshToken.cs` | Factory method, private setters, behaviour on the entity (`Revoke`, `IsActive`) |
| `Domain/Features/Auth/IRefreshTokenRepository.cs` | Extending `IRepository<T>` with feature-specific queries |
| `Application/Features/Auth/AuthService.cs` | Full service pattern: validate → act → save → `Result<T>`, with `LoggerMessage` logging |
| `Application/Features/Auth/Validators/AuthRuleBuilderExtensions.cs` | Sharing rules across validators |
| `Application/Common/Identity/IIdentityService.cs` | Keeping a framework (Identity) out of the upper layers behind an owned interface |
| `Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs` | EF Fluent API, query filter, unique index |
| `Infrastructure/Identity/IdentityService.cs` | Translating a framework's result type into `Result` |
| `API/Controllers/AuthController.cs` | 1-line action methods |

### 7.1 The Identity Seam

ASP.NET Core Identity is EF-flavoured, and Domain must stay dependency-free. So Identity types are
**confined to Infrastructure** and the layers above talk to interfaces this project owns:

```
AuthService  ──►  IIdentityService   ──►  IdentityService  ──►  UserManager<ApplicationUser>
(Application)     (Application)           (Infrastructure)      (ASP.NET Core Identity)

                  ITokenService      ──►  JwtTokenService
                  ICurrentUser       ──►  CurrentUser (API — the only layer with HttpContext)
                  IEmailSender       ──►  LoggingEmailSender (swap for SMTP later)
```

`IIdentityService` returns `AppUserInfo`, `Result`, and primitives — never an Identity type.
**Need a user operation from a service? Add a method there. Don't inject `UserManager`.**

### 7.2 Session Mechanics

- **Access token** — JWT, 15 minutes, claims `sub` / `email` / `jti` / `role`. Not revocable, hence short.
- **Refresh token** — 32 random bytes, 14 days, single-use. Only its SHA-256 hash is stored.
- **Rotation** — every `/auth/refresh` revokes the presented token and issues a new pair.
- **Reuse detection** — presenting an already-revoked token means the chain leaked, so *every*
  session for that user is revoked, including the legitimate holder's newest one. Better a forced
  re-login than a live stolen session.

Two things are easy to break without noticing:

1. The bearer handler sets `MapInboundClaims = false` and `RoleClaimType = "role"` to match the short
   claim names we issue. Change one side only and `[Authorize(Roles = ...)]` stops matching **silently**.
2. `[AllowAnonymous]` at controller level overrides `[Authorize]` on individual actions. On
   `AuthController` it is applied per action for exactly this reason.

---

## 8. Testing Strategy

### Unit Tests (`tests/MangaStore.UnitTests/`)

- Mock repositories with **NSubstitute**
- Assert with **Shouldly**
- Test services in isolation — no HTTP, no database

```csharp
[Fact]
public async Task CreateAsync_Returns201_WithValidRequest()
{
    // Arrange
    var repo = Substitute.For<IProductRepository>();
    var uow = Substitute.For<IUnitOfWork>();
    var service = new ProductService(repo, uow, mapper, validationService, logger);

    // Act
    var result = await service.CreateAsync(new CreateProductRequest("Widget", "Desc", 9.99m));

    // Assert
    result.IsSuccess.ShouldBeTrue();
    result.Value.Name.ShouldBe("Widget");
}
```

### Integration Tests (`tests/MangaStore.IntegrationTests/`)

- `WebApplicationFactory<Program>` + SQLite in-memory
- `IAsyncLifetime` resets the schema before each test (`EnsureDeleted` + `EnsureCreated`)
- Tests the full HTTP pipeline: middleware → controller → service → db
- Verifies soft-delete actually sets `IsDeleted = true` in the database

```csharp
[Fact]
public async Task DELETE_Returns204_AndSoftDeletesRow()
{
    var created = await CreateProductAsync("Widget", "Desc", 9.99m);
    var deleteResponse = await _client.DeleteAsync($"/api/v1/products/{created.Id}");
    deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

    // Verify row is soft-deleted (bypasses query filter)
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var row = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == created.Id);
    row!.IsDeleted.ShouldBeTrue();
    row.UpdatedAt.ShouldNotBeNull();
}
```

---

## 9. Configuration & Secrets

Never commit secrets. The app fails at startup if required config is missing (`ValidateOnStart()`).

**Development setup (local only, not committed):**

```bash
# Set user secrets for development
dotnet user-secrets set "Jwt:Secret" "your-dev-secret-at-least-32-chars" --project src/MangaStore.API
dotnet user-secrets set "Jwt:Issuer" "MangaStore"                   --project src/MangaStore.API
dotnet user-secrets set "Jwt:Audience" "MangaStore"                  --project src/MangaStore.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=MangaStore;Trusted_Connection=True;" --project src/MangaStore.API
```

**Production:** Use environment variables or Azure Key Vault:
```
Jwt__Secret=...
Jwt__Issuer=...
ConnectionStrings__DefaultConnection=...
Cors__AllowedOrigins__0=https://myapp.com
```

**Required config keys:**

| Key | Required | Default | Notes |
|-----|----------|---------|-------|
| `Jwt:Secret` | ✓ | — | Min 32 characters |
| `Jwt:Issuer` | ✓ | — | |
| `Jwt:Audience` | ✓ | — | |
| `ConnectionStrings:DefaultConnection` | ✓ | — | SQL Server connection string |
| `Database:Provider` | — | `SqlServer` | `SqlServer` or `Sqlite` |
| `Cors:AllowedOrigins` | ✓ | — | Array, at least 1 origin |
| `RateLimit:PermitLimit` | — | `100` | Requests per window |
| `RateLimit:WindowSeconds` | — | `60` | Window length |

---

## 10. Common Mistakes to Avoid

| ❌ Wrong | ✓ Correct |
|---------|----------|
| Calling `_mapper.Map<>()` in a controller | Call it only inside the service |
| Calling `_repo.Update(entity)` after loading it | Just mutate the entity — EF tracks it |
| Throwing `NotFoundException` from a service | Return `ResultError.NotFound(...)` from the service |
| Putting `if/else` logic inside a controller | Move all logic to the service |
| Adding `[Column]`, `[MaxLength]` on an entity | Use Fluent API in `XxxConfiguration.cs` |
| Accessing `result.Value` without checking `IsSuccess` | Always check `result.IsSuccess` first |
| Adding `WHERE IsDeleted = 0` in queries | The global query filter handles this automatically |
| Forgetting `HasQueryFilter` in entity config | Deleted rows become visible to all queries |
| Calling `services.AddScoped<IOrderService, OrderService>()` manually | Scrutor registers it automatically |
| Domain events running inside a transaction | Events dispatch POST-commit — listeners must be idempotent |
