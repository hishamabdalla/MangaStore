namespace MangaStore.Domain.Features.Auth;

using MangaStore.Domain.Common;

/// <summary>A long-lived credential that exchanges for a new access token. Rotated on every use.</summary>
/// <remarks>
/// Only the SHA-256 hash of the token is stored, so a database leak yields nothing usable.
/// <see cref="UserId"/> is a bare identifier rather than a navigation property because the user
/// aggregate is an ASP.NET Core Identity type living in the Infrastructure layer.
/// </remarks>
public sealed class RefreshToken : BaseEntity
{
    private RefreshToken() { }

    /// <summary>Gets the SHA-256 hash of the token; the raw value is never persisted.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Gets the identifier of the user this token authenticates.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the UTC instant after which this token can no longer be exchanged.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Gets the UTC instant this token was revoked, or <see langword="null"/> if still valid.</summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Gets the hash of the token issued in this one's place during rotation.</summary>
    public string? ReplacedByTokenHash { get; private set; }

    /// <summary>Gets the IP address the token was issued to, for audit purposes.</summary>
    public string? CreatedByIp { get; private set; }

    /// <summary>Creates and returns a new refresh token.</summary>
    /// <param name="tokenHash">SHA-256 hash of the raw token.</param>
    /// <param name="userId">Owner of the token.</param>
    /// <param name="expiresAt">UTC expiry instant.</param>
    /// <param name="createdByIp">Requesting IP address, or <see langword="null"/> if unknown.</param>
    public static RefreshToken Create(string tokenHash, Guid userId, DateTime expiresAt, string? createdByIp) =>
        new()
        {
            TokenHash = tokenHash,
            UserId = userId,
            ExpiresAt = expiresAt,
            CreatedByIp = createdByIp,
        };

    /// <summary>Returns <see langword="true"/> if the token is neither revoked nor expired at <paramref name="utcNow"/>.</summary>
    /// <param name="utcNow">The current UTC instant.</param>
    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;

    /// <summary>Marks the token as revoked. Revoking an already-revoked token leaves the original instant intact.</summary>
    /// <param name="utcNow">The current UTC instant.</param>
    /// <param name="replacedByTokenHash">Hash of the replacement token when revoking as part of rotation.</param>
    public void Revoke(DateTime utcNow, string? replacedByTokenHash = null)
    {
        RevokedAt ??= utcNow;
        ReplacedByTokenHash ??= replacedByTokenHash;
    }
}
