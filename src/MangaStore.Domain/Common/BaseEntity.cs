namespace MangaStore.Domain.Common;

using MangaStore.Domain.Events;

/// <summary>Base class for all domain entities. Provides identity, audit timestamps, soft-delete, and domain event support.</summary>
public abstract class BaseEntity : IAuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Gets the entity's unique identifier.</summary>
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc/>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Gets a value indicating whether this entity has been soft-deleted.</summary>
    public bool IsDeleted { get; internal set; }

    /// <summary>Gets domain events raised during this entity's lifetime. Cleared by the infrastructure layer after dispatch.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Queues a domain event for dispatch after the current unit of work is committed.</summary>
    /// <param name="domainEvent">The event to enqueue.</param>
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Removes all queued domain events.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
