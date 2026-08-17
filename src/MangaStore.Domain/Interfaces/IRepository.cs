namespace MangaStore.Domain.Interfaces;

using MangaStore.Domain.Common;

/// <summary>Generic repository contract for <typeparamref name="TEntity"/> aggregate roots.</summary>
/// <typeparam name="TEntity">A <see cref="BaseEntity"/> subtype.</typeparam>
public interface IRepository<TEntity> where TEntity : BaseEntity
{
    /// <summary>Returns the entity with the given <paramref name="id"/>, or <see langword="null"/> if not found.</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns all entities visible to the current EF Core query filter.</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a page of entities ordered by <see cref="BaseEntity.CreatedAt"/> descending.</summary>
    /// <param name="skip">Number of records to skip.</param>
    /// <param name="take">Maximum number of records to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<TEntity>> GetPagedAsync(int skip, int take, CancellationToken ct = default);

    /// <summary>Returns the total count of entities visible to the current EF Core query filter.</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Stages a new entity for insertion on the next <c>SaveChanges</c>.</summary>
    Task AddAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>Soft-deletes the entity by staging it for removal; the <c>SoftDeleteInterceptor</c> converts this to an <c>IsDeleted</c> flag.</summary>
    void Delete(TEntity entity);

    /// <summary>Returns <see langword="true"/> if an entity with the given <paramref name="id"/> exists.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
