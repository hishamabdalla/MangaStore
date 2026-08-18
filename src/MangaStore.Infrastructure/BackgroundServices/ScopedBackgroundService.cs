namespace MangaStore.Infrastructure.BackgroundServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Base class for recurring background jobs that need scoped services such as <c>AppDbContext</c>.</summary>
/// <remarks>
/// <para>
/// Handles the three things every recurring job in this system needs and each would otherwise
/// re-implement: a fresh <see cref="IServiceScope"/> per tick, per-tick exception isolation so one
/// bad iteration cannot take the host down, and jitter on the interval.
/// </para>
/// <para>
/// Jitter is not decoration. Several jobs all waking on the same tick is self-inflicted load, and
/// the resulting spikes are hard to attribute once the system is under real traffic.
/// </para>
/// </remarks>
public abstract partial class ScopedBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>Initialises the job with the collaborators every derived job needs.</summary>
    /// <param name="scopeFactory">Creates the per-tick dependency injection scope.</param>
    /// <param name="timeProvider">Supplies the delay between ticks; injectable so tests need not wait in real time.</param>
    /// <param name="logger">Receives per-tick failures.</param>
    protected ScopedBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Gets the base interval between ticks, before jitter is applied.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>Gets the maximum proportion of <see cref="Interval"/> added as random jitter. Defaults to <c>0.2</c>.</summary>
    protected virtual double JitterFactor => 0.2;

    /// <summary>Runs one iteration of the job.</summary>
    /// <param name="services">Service provider scoped to this tick.</param>
    /// <param name="ct">Token signalled when the host is shutting down.</param>
    protected abstract Task ExecuteTickAsync(IServiceProvider services, CancellationToken ct);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await ExecuteTickAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown, not a failure.
                break;
            }
            catch (Exception ex)
            {
                // Swallowed deliberately: a single failed tick must not terminate the host. The
                // next tick retries, and the log is the signal that something needs attention.
                Log.TickFailed(_logger, GetType().Name, ex);
            }

            try
            {
                await Task.Delay(NextDelay(), _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TimeSpan NextDelay()
    {
        double jitter = Random.Shared.NextDouble() * JitterFactor * Interval.TotalMilliseconds;
        return Interval + TimeSpan.FromMilliseconds(jitter);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "{Job} tick failed; continuing.")]
        public static partial void TickFailed(ILogger logger, string job, Exception exception);
    }
}
