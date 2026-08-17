namespace MangaStore.Infrastructure.Services;

using MangaStore.Domain.Interfaces;

/// <inheritdoc cref="IDateTime"/>
public sealed class SystemDateTime : IDateTime
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new instance of <see cref="SystemDateTime"/>.</summary>
    /// <param name="timeProvider">The time provider to use; defaults to <see cref="TimeProvider.System"/> when resolved from DI.</param>
    public SystemDateTime(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
