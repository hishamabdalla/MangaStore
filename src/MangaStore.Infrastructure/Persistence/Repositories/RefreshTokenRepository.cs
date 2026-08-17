namespace MangaStore.Infrastructure.Persistence.Repositories;

using MangaStore.Domain.Features.Auth;
using Microsoft.EntityFrameworkCore;

/// <inheritdoc cref="IRefreshTokenRepository"/>
public sealed class RefreshTokenRepository : GenericRepository<RefreshToken>, IRefreshTokenRepository
{
    /// <summary>Initialises a new instance of <see cref="RefreshTokenRepository"/>.</summary>
    public RefreshTokenRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        await Context.Set<RefreshToken>()
            .AsTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct = default) =>
        await Context.Set<RefreshToken>()
            .AsTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > utcNow)
            .ToListAsync(ct);
}
