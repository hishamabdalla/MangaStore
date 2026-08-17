namespace MangaStore.Domain.Interfaces;

/// <summary>Commits all pending changes tracked by the current database context.</summary>
public interface IUnitOfWork
{
    /// <summary>Persists all tracked changes and returns the number of affected rows.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
