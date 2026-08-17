namespace MangaStore.Domain.Events;

/// <summary>Marker interface for all domain events raised by aggregate roots.</summary>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier of this event instance.</summary>
    Guid EventId { get; }

    /// <summary>Gets the UTC timestamp when the event was raised.</summary>
    DateTime OccurredOn { get; }
}
