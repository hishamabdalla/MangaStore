# Entity Factory vs AutoMapper — Why Entities Own Their Construction

## The Problem With Mapping Into an Entity

When AutoMapper creates an object it needs to set properties. To do that your entity must expose public or at least `private set` setters:

```csharp
// To let mapper work, you'd need this:
public string Name { get; set; }        // anyone can set it anywhere
public decimal Price { get; set; }      // no protection at all
```

Now any code anywhere can do:

```csharp
product.Name = "";        // invalid — but compiles fine
product.Price = -999;     // invalid — but compiles fine
```

The entity has no way to enforce its own rules because it gave away control of its state.

With a factory:

```csharp
public string Name { get; private set; }
public decimal Price { get; private set; }

public static Product Create(string name, string? description, decimal price)
{
    var product = new Product { Name = name, Description = description, Price = price };
    product.AddDomainEvent(new ProductCreatedEvent(product.Id));
    return product;
}
```

The only way to create a valid `Product` is through `Create()`. No other path exists.

---

## Domain Events — The Killer Argument

```csharp
product.AddDomainEvent(new ProductCreatedEvent(product.Id));
```

A domain event means something significant happened in the business. "A product was created" is a business fact — listeners may send emails, update search indexes, invalidate caches, write audit logs.

AutoMapper knows nothing about this. It just assigns properties. If you map a DTO to a `Product`, the event **never fires**. You have silently broken the domain model.

The factory guarantees: every product that comes into existence raises the event. No exceptions, no "I forgot to call AddDomainEvent in the service".

---

## Invariants — Rules That Must Always Be True

Imagine you add a rule: a product's price must be greater than zero.

**With a factory:**

```csharp
public static Product Create(string name, string? description, decimal price)
{
    if (price <= 0) throw new DomainException("Price must be positive.");
    // ...
}
```

One place. Always enforced. Cannot be bypassed.

**With a mapper — where do you put this rule?**

| Option | Problem |
|--------|---------|
| FluentValidation validator | Validation is an application concern, not a domain invariant. It can be skipped. A validator on `CreateProductRequest` does not protect you when a `Product` is constructed from a different path. |
| AutoMapper profile | Completely wrong layer. AutoMapper is for projection, not business logic. |
| The service | Scattered and duplicated if multiple services create products. |

---

## The `Update()` Method — Same Principle

```csharp
public void Update(string name, string? description, decimal price)
{
    Name = name;
    Description = description;
    Price = price;
}
```

Why not just let the mapper overwrite the properties directly?

Because tomorrow you might need:

```csharp
public void Update(string name, string? description, decimal price)
{
    if (Name != name)
        AddDomainEvent(new ProductRenamedEvent(Id, Name, name));

    Name = name;
    Description = description;
    Price = price;
}
```

Now renaming raises an event. If you were using a mapper, you can never add this without refactoring every call site. With the method, you add it once and every caller gets it for free.

---

## Where AutoMapper Does Belong

AutoMapper is perfect for reading — projecting an entity to a DTO for a response:

```csharp
// Entity → DTO: pure projection, no rules, mapper is ideal
return _mapper.Map<ProductDto>(product);
```

This is safe because:

- You are not creating or mutating domain state
- No events need to fire
- No invariants can be violated
- It is just copying fields for the caller to read

---

## Summary

| | Factory / Method | AutoMapper |
|---|---|---|
| Enforces invariants | Yes — one place | No — scattered or missing |
| Raises domain events | Yes — guaranteed | No — silently skipped |
| Encapsulates state | Yes — `private set` | No — needs public setters |
| Adding rules later | Change one method | Refactor every call site |
| Right direction | Entity construction (in) | DTO projection (out) |

**The rule:** Domain objects control how they come to life and how they change. That is the entire point of having a Domain layer. Handing that control to a mapper is giving away the most valuable thing the domain model offers.
