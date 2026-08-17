namespace MangaStore.Infrastructure.Persistence;

using Microsoft.Extensions.Logging;
using MangaStore.Application.Common.Events;
using MangaStore.Domain.Common;
using MangaStore.Domain.Interfaces;

/// <inheritdoc cref="IUnitOfWork"/>
/// <remarks>
/// Domain events are dispatched after <c>SaveChanges</c> commits. Because there is no outer transaction,
/// listeners must be <strong>idempotent</strong>: a failed listener will not roll back the database write.
/// </remarks>
public sealed partial class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly ILogger<UnitOfWork> _logger;

    /// <summary>Initialises a new instance of <see cref="UnitOfWork"/>.</summary>
    public UnitOfWork(AppDbContext context, IDomainEventDispatcher dispatcher, ILogger<UnitOfWork> logger)
    {
        _context = context;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var events = _context.ChangeTracker
            .Entries<BaseEntity>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        foreach (var entry in _context.ChangeTracker.Entries<BaseEntity>())
        {
            entry.Entity.ClearDomainEvents();
        }

        int rowsAffected = await _context.SaveChangesAsync(ct);

        try
        {
            await _dispatcher.DispatchAsync(events, ct);
        }
        catch (Exception ex)
        {
            Log.DispatchFailed(_logger, ex);
        }

        return rowsAffected;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "One or more domain event listeners failed after commit. The database write succeeded; listeners must be idempotent.")]
        public static partial void DispatchFailed(ILogger logger, Exception exception);
    }
}
