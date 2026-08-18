# MangaStore

A production-ready **.NET 10 Web API** template built with **Clean Architecture** and a plain service layer pattern. No MediatR, no CQRS, no Minimal APIs. Clone, rename, and ship.

---

## Roadmap

The digital gift-card store is being built on this template in 14 documented phases.
Start with **[`docs/phases/00-overview-and-decisions.md`](docs/phases/00-overview-and-decisions.md)** — it decodes the
existing spreadsheet workflow, lists what it gets wrong, and carries the register of open
business decisions.

---

## Tech Stack

| Concern | Library |
|---|---|
| Framework | ASP.NET Core 10 |
| ORM | Entity Framework Core 10 (SQL Server) |
| Mapping | AutoMapper 16 |
| Validation | FluentValidation 12 |
| DI scanning | Scrutor 5 |
| Authentication | JWT Bearer |
| API versioning | Asp.Versioning 8 |
| API docs | Swashbuckle 7 (Swagger UI) + Scalar 2 |
| Logging | Serilog (console + file) |
| Unit tests | xUnit + NSubstitute + Shouldly |
| Integration tests | `WebApplicationFactory` + SQLite in-memory |

---

## Architecture

```
API → Application → Domain
Infrastructure → Application → Domain
Domain → (nothing)
```

| Layer | Responsibility |
|---|---|
| **Domain** | Entities, domain events, repository interfaces, domain exceptions. Zero external dependencies. |
| **Application** | Service interfaces + implementations, DTOs, FluentValidation validators, AutoMapper profiles, `Result<T>`, pagination, `IValidationService`. |
| **Infrastructure** | `AppDbContext`, EF Core Fluent API configurations, repository implementations, `UnitOfWork`, soft-delete & audit interceptors, `TimeProvider` abstraction. |
| **API** | Controllers, `IExceptionHandler`, `Program.cs`, DI wiring. Controllers are dumb — one service call, one result, return. |

### Project Structure

```
src/
├── MangaStore.Domain/          # Zero-dependency enterprise rules
├── MangaStore.Application/     # Use cases, services, DTOs, validators
├── MangaStore.Infrastructure/  # EF Core, repositories, interceptors
└── MangaStore.API/             # Controllers, Program.cs, appsettings

tests/
├── MangaStore.UnitTests/       # Service tests with NSubstitute mocks
└── MangaStore.IntegrationTests/# Full HTTP tests against SQLite in-memory
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server, SQL Server LocalDB, or Docker

---

## Getting Started

### 1. Clone and rename

```bash
git clone <repo-url> MyProject
cd MyProject
```

Replace all occurrences of `MangaStore` with your project name in solution files, namespaces, and `appsettings.json`.

### 2. Restore tools and packages

```bash
dotnet tool restore   # installs dotnet-ef from .config/dotnet-tools.json
dotnet restore
```

### 3. Configure secrets

The JWT secret must be supplied outside of committed files. Use user secrets for local dev:

```bash
cd src/MangaStore.API
dotnet user-secrets set "Jwt:Secret" "your-super-secret-key-at-least-32-chars"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\MSSQLLocalDB;Database=MyProjectDb;Trusted_Connection=True;"
```

Or override via environment variables in CI/CD and production.

### 4. Apply EF Core migrations

```bash
dotnet ef database update --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API
```

> An `InitialCreate` migration is included. Rename the project first, then add a new migration if you have made schema changes:
> `dotnet ef migrations add YourMigrationName --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API`

### 5. Run

```bash
dotnet run --project src/MangaStore.API
```

Open **Swagger UI** at `https://localhost:{port}/swagger` or **Scalar** at `https://localhost:{port}/scalar/v1`.

### 6. Run tests

```bash
dotnet test
```

---

## Key Patterns

### Result pattern

Services never throw. Every method returns `Result<T>` or `Result`:

```csharp
public async Task<Result<ProductDto>> GetByIdAsync(Guid id, CancellationToken ct)
{
    var product = await _productRepository.GetByIdAsync(id, ct);
    if (product is null)
        return ResultError.NotFound<Product>(id);

    return _mapper.Map<ProductDto>(product);
}
```

`ApiControllerBase` maps error codes to HTTP status codes automatically:

| `ResultError` | HTTP |
|---|---|
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Forbidden` | 403 |
| `Validation` | 422 |
| `Failure` | 400 |

### Controller rule

A controller method does exactly three things — call a service, pass the result to a handler, return:

```csharp
[HttpGet("{id:guid}")]
[ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct) =>
    HandleResult(await _productService.GetByIdAsync(id, ct));
```

### Soft delete & audit

All entities inherit `BaseEntity`. EF Core interceptors set `CreatedAt`, `UpdatedAt`, and `IsDeleted` automatically — no manual assignment needed.

`HasQueryFilter(e => !e.IsDeleted)` in every entity configuration excludes soft-deleted rows from all queries automatically.

### Domain events

Entities raise domain events via `AddDomainEvent(new MyEvent(...))`. Events are collected on `BaseEntity.DomainEvents`, dispatched after `SaveChanges`, and cleared by the infrastructure layer. Listeners must be **idempotent** — dispatch happens post-commit with no outer transaction guarantee.

---

## Configuration Reference

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Database": {
    "Provider": "SqlServer"
  },
  "Jwt": {
    "Issuer": "MangaStore",
    "Audience": "MangaStore",
    "Secret": ""
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:4200"]
  },
  "RateLimit": {
    "WindowSeconds": 60,
    "PermitLimit": 100
  }
}
```

All sensitive values (`Jwt:Secret`, `ConnectionStrings:DefaultConnection`) must be supplied via user secrets, environment variables, or a secrets manager. The app will **fail to start** if `Jwt:Secret` is missing or shorter than 32 characters.

---

## Adding a New Feature

Use **Order** as a walkthrough example.

### 1. Domain entity

`src/MangaStore.Domain/Features/Orders/Order.cs`

```csharp
/// <summary>Represents a customer order.</summary>
public sealed class Order : BaseEntity
{
    private Order() { }

    /// <summary>Gets the name of the customer who placed the order.</summary>
    public string CustomerName { get; private set; } = string.Empty;

    /// <summary>Gets the total monetary amount of the order.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Creates a new order and raises <see cref="OrderCreatedEvent"/>.</summary>
    public static Order Create(string customerName, decimal totalAmount)
    {
        var order = new Order { CustomerName = customerName, TotalAmount = totalAmount };
        order.AddDomainEvent(new OrderCreatedEvent(order.Id));
        return order;
    }

    /// <summary>Replaces all mutable fields with new values.</summary>
    public void Update(string customerName, decimal totalAmount)
    {
        CustomerName = customerName;
        TotalAmount = totalAmount;
    }
}
```

### 2. Domain event (optional)

```csharp
/// <summary>Raised when a new order is created.</summary>
/// <param name="OrderId">Identifier of the newly created order.</param>
public sealed record OrderCreatedEvent(Guid OrderId) : DomainEvent;
```

### 3. Repository interface

```csharp
/// <summary>Data access contract for <see cref="Order"/> entities.</summary>
public interface IOrderRepository : IRepository<Order> { }
```

### 4–5. Application layer (DTOs, validators, profile, service)

Create `Application/Features/Orders/` with:
- `Dtos/OrderDto.cs`, `CreateOrderRequest.cs`, `UpdateOrderRequest.cs`
- `Profiles/OrderProfile.cs` — `CreateMap<Order, OrderDto>()`
- `Validators/CreateOrderRequestValidator.cs`, `UpdateOrderRequestValidator.cs`
- `IOrderService.cs` + `OrderService.cs` returning `Result<T>`

### 6. EF Core configuration

```csharp
/// <summary>Configures the <see cref="Order"/> entity.</summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasQueryFilter(o => !o.IsDeleted);
        builder.Ignore(o => o.DomainEvents);
    }
}
```

### 7. Repository + DbSet

```csharp
/// <inheritdoc cref="IOrderRepository"/>
public sealed class OrderRepository(AppDbContext context)
    : GenericRepository<Order>(context), IOrderRepository { }
```

Add to `AppDbContext`: `public DbSet<Order> Orders => Set<Order>();`

### 8. Migration

```bash
dotnet ef migrations add AddOrders --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API
dotnet ef database update --project src/MangaStore.Infrastructure --startup-project src/MangaStore.API
```

### 9. Controller

```csharp
/// <summary>Manages order resources.</summary>
public sealed class OrdersController(IOrderService orderService) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct) =>
        HandleResult(await orderService.GetByIdAsync(id, ct));
}
```

> **No manual DI registration needed.** Scrutor auto-discovers `OrderService` (ends with `Service`) and `OrderRepository` (ends with `Repository`).

---

## API Versioning

All endpoints use URL-segment versioning: `/api/v{version}/...`. Current version: **v1**.

## Authentication

JWT Bearer is pre-wired. Add `[Authorize]` to controllers or actions as needed. No login endpoint is included — add an `AuthController` that issues tokens from your user store.

## Naming Conventions

| Artifact | Pattern |
|---|---|
| Service interface / impl | `IProductService` / `ProductService` |
| Repository interface / impl | `IProductRepository` / `ProductRepository` |
| Read DTO | `ProductDto` |
| Create / update requests | `CreateProductRequest` / `UpdateProductRequest` |
| Validators | `CreateProductRequestValidator` / `UpdateProductRequestValidator` |
| AutoMapper profile | `ProductProfile` |
| Controller | `ProductsController` (plural) |
| Domain event | `ProductCreatedEvent` |
| EF configuration | `ProductConfiguration` |

## Rules

**Do:**
- Return `Result<T>` from every service method.
- Validate via `IValidationService` before any business logic.
- Call `_unitOfWork.SaveChangesAsync(ct)` at the end of every write.
- Use `<inheritdoc/>` on every concrete implementation's methods.
- Use `[ProducesResponseType<T>(statusCode)]` on every controller action.
- Put `CancellationToken` in every `async` signature.
- Put `HasQueryFilter(e => !e.IsDeleted)` in every entity's EF configuration.

**Don't:**
- Don't throw from services — return `Result.Fail(...)`.
- Don't call `_mapper.Map<>` in a controller.
- Don't call repositories from controllers.
- Don't put EF Core or AutoMapper in Domain or Application.
- Don't reference Infrastructure from Application or Domain.
- Don't add MediatR or any CQRS library.
- Don't register services or repositories manually — Scrutor handles it.
- Don't use `Forbid()` for 403 — use `HandleResult` with `ResultError.Forbidden`.

---

## License

MIT
