namespace MangaStore.Domain.Events;

/// <summary>Base class for domain events. Assigns a unique <see cref="EventId"/> and captures <see cref="OccurredOn"/> at construction time.</summary>
public abstract class DomainEvent : IDomainEvent
{
    /// <summary>Initialises a new domain event with a new identifier and the current UTC time.</summary>
    protected DomainEvent() : this(DateTime.UtcNow) { }

    /// <summary>Initialises a new domain event with a new identifier and the supplied timestamp.</summary>
    /// <param name="occurredOn">UTC timestamp to record as the occurrence time; use in tests for deterministic values.</param>
    protected DomainEvent(DateTime occurredOn)
    {
        EventId = Guid.NewGuid();
        OccurredOn = occurredOn;
    }

    /// <inheritdoc/>
    public Guid EventId { get; }

    /// <inheritdoc/>
    public DateTime OccurredOn { get; }
}
