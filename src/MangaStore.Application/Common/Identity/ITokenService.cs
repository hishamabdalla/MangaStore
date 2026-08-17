namespace MangaStore.Application.Common.Identity;

/// <summary>Issues the access and refresh tokens that make up a session.</summary>
public interface ITokenService
{
    /// <summary>Creates a signed, short-lived JWT access token.</summary>
    /// <param name="userId">Value of the <c>sub</c> claim.</param>
    /// <param name="email">Value of the <c>email</c> claim.</param>
    /// <param name="roles">Role names emitted as <c>role</c> claims.</param>
    /// <returns>The encoded token and the UTC instant it expires.</returns>
    (string Token, DateTime ExpiresAt) CreateAccessToken(Guid userId, string email, IReadOnlyList<string> roles);

    /// <summary>Creates a cryptographically random refresh token.</summary>
    /// <returns>The raw token, returned to the caller once and never stored, alongside the hash that is persisted and the UTC expiry.</returns>
    (string Raw, string Hash, DateTime ExpiresAt) CreateRefreshToken();

    /// <summary>Hashes a raw refresh token so it can be compared against stored values.</summary>
    /// <param name="rawToken">The token as presented by the client.</param>
    string Hash(string rawToken);
}
