namespace MangaStore.Application.Common.Events;

using MangaStore.Domain.Events;

/// <summary>Handles domain events of type <typeparamref name="TEvent"/> after they have been committed to the database.</summary>
/// <typeparam name="TEvent">The concrete domain event type.</typeparam>
public interface IDomainEventListener<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>Processes the given <paramref name="domainEvent"/>.</summary>
    /// <param name="domainEvent">The event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
