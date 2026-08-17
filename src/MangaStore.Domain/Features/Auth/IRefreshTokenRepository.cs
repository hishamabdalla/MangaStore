namespace MangaStore.Domain.Features.Auth;

using MangaStore.Domain.Interfaces;

/// <summary>Persistence contract for <see cref="RefreshToken"/>.</summary>
public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    /// <summary>Returns the token with the given <paramref name="tokenHash"/>, or <see langword="null"/> if unknown.</summary>
    /// <remarks>Returns revoked and expired tokens too — the caller decides, so that a presented-but-revoked token can be detected as reuse.</remarks>
    /// <param name="tokenHash">SHA-256 hash of the presented token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Returns every token for <paramref name="userId"/> that is neither revoked nor expired.</summary>
    /// <param name="userId">Owner of the tokens.</param>
    /// <param name="utcNow">The current UTC instant used to test expiry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);
}
