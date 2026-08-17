namespace MangaStore.Infrastructure.Persistence;

using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MangaStore.Application.Common.Events;
using MangaStore.Domain.Events;

/// <inheritdoc cref="IDomainEventDispatcher"/>
public sealed partial class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, ILogger, IDomainEvent, CancellationToken, Task>> _invokerCache = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    /// <summary>Initialises a new instance of <see cref="DomainEventDispatcher"/>.</summary>
    public DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var domainEvent in events)
        {
            var invoker = _invokerCache.GetOrAdd(domainEvent.GetType(), BuildInvoker);

            await invoker(_serviceProvider, _logger, domainEvent, ct);
        }
    }

    private static Func<IServiceProvider, ILogger, IDomainEvent, CancellationToken, Task> BuildInvoker(Type eventType)
    {
        var listenerType = typeof(IDomainEventListener<>).MakeGenericType(eventType);
        var handleMethod = listenerType.GetMethod(nameof(IDomainEventListener<IDomainEvent>.HandleAsync))!;

        return async (sp, logger, evt, ct) =>
        {
            var listeners = sp.GetServices(listenerType);
            foreach (object? listener in listeners)
            {
                try
                {
                    await (Task)handleMethod.Invoke(listener, [evt, ct])!;
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    Log.ListenerFailed(logger, listener!.GetType().Name, eventType.Name, tie.InnerException);
                }
                catch (Exception ex)
                {
                    Log.ListenerFailed(logger, listener!.GetType().Name, eventType.Name, ex);
                }
            }
        };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Domain event listener {ListenerType} failed for event {EventType}.")]
        public static partial void ListenerFailed(ILogger logger, string listenerType, string eventType, Exception exception);
    }
}
