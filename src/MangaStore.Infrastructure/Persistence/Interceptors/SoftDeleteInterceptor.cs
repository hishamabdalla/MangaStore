namespace MangaStore.Infrastructure.Persistence.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MangaStore.Domain.Common;
using MangaStore.Domain.Interfaces;

/// <summary>Converts hard-delete EF Core state changes into soft-deletes by setting <see cref="BaseEntity.IsDeleted"/> to <see langword="true"/> and stamping <see cref="IAuditableEntity.UpdatedAt"/>.</summary>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly IDateTime _dateTime;

    /// <summary>Initialises a new instance of <see cref="SoftDeleteInterceptor"/>.</summary>
    public SoftDeleteInterceptor(IDateTime dateTime)
    {
        _dateTime = dateTime;
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ApplySoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ApplySoftDelete(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplySoftDelete(DbContext? context)
    {
        if (context is null) return;

        var now = _dateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>().Where(e => e.State == EntityState.Deleted))
        {
            entry.Entity.IsDeleted = true;
            entry.Entity.UpdatedAt = now;
            entry.State = EntityState.Modified;
        }
    }
}
