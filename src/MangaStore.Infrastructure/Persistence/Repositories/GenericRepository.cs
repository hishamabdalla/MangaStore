namespace MangaStore.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using MangaStore.Domain.Common;
using MangaStore.Domain.Interfaces;

/// <summary>Default implementation of <see cref="IRepository{TEntity}"/> backed by <see cref="AppDbContext"/>.</summary>
/// <typeparam name="TEntity">A <see cref="BaseEntity"/> subtype.</typeparam>
public class GenericRepository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
{
    private readonly AppDbContext _context;

    /// <summary>Initialises a new instance of <see cref="GenericRepository{TEntity}"/>.</summary>
    public GenericRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Gets the underlying <see cref="AppDbContext"/> for use by derived repositories.</summary>
    protected AppDbContext Context => _context;

    /// <inheritdoc/>
    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.Set<TEntity>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <inheritdoc/>
    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Set<TEntity>().AsNoTracking().ToListAsync(ct);

    /// <inheritdoc/>
    public virtual async Task<IReadOnlyList<TEntity>> GetPagedAsync(int skip, int take, CancellationToken ct = default) =>
        await _context.Set<TEntity>()
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public virtual async Task<int> CountAsync(CancellationToken ct = default) =>
        await _context.Set<TEntity>().AsNoTracking().CountAsync(ct);

    /// <inheritdoc/>
    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        await _context.Set<TEntity>().AddAsync(entity, ct);

    /// <inheritdoc/>
    public virtual void Delete(TEntity entity) =>
        _context.Set<TEntity>().Remove(entity);

    /// <inheritdoc/>
    public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await _context.Set<TEntity>().AsNoTracking().AnyAsync(e => e.Id == id, ct);
}
