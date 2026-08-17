namespace MangaStore.Application.Common.Events;

using MangaStore.Domain.Events;

/// <summary>Dispatches collected domain events to all registered <see cref="IDomainEventListener{TEvent}"/> implementations after a unit of work is committed.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches each event in <paramref name="events"/> to every registered listener.</summary>
    /// <param name="events">The events to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
