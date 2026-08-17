namespace MangaStore.Infrastructure.Persistence.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MangaStore.Domain.Common;
using MangaStore.Domain.Interfaces;

/// <summary>Sets <see cref="IAuditableEntity.CreatedAt"/> on insert and <see cref="IAuditableEntity.UpdatedAt"/> on update before each <c>SaveChanges</c>.</summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly IDateTime _dateTime;

    /// <summary>Initialises a new instance of <see cref="AuditInterceptor"/>.</summary>
    public AuditInterceptor(IDateTime dateTime)
    {
        _dateTime = dateTime;
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        UpdateAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        UpdateAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateAuditFields(DbContext? context)
    {
        if (context is null) return;

        var now = _dateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
