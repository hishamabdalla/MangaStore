namespace MangaStore.Infrastructure.Security;

using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Common.Options;
using MangaStore.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <inheritdoc cref="ITokenService"/>
public sealed class JwtTokenService : ITokenService
{
    /// <summary>Refresh token entropy in bytes. 256 bits makes guessing infeasible.</summary>
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options;
    private readonly IDateTime _dateTime;
    private readonly SigningCredentials _signingCredentials;

    /// <summary>Initialises a new instance of <see cref="JwtTokenService"/>.</summary>
    public JwtTokenService(IOptions<JwtOptions> options, IDateTime dateTime)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _dateTime = dateTime;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc/>
    public (string Token, DateTime ExpiresAt) CreateAccessToken(Guid userId, string email, IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var now = _dateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>(roles.Count + 3)
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),

            // A per-token identifier, so an individual access token can be denylisted later if needed.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // Short "role" claim names, matching the RoleClaimType configured on the bearer handler.
        claims.AddRange(roles.Select(role => new Claim(AppClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = _signingCredentials,
        };

        string token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expiresAt);
    }

    /// <inheritdoc/>
    public (string Raw, string Hash, DateTime ExpiresAt) CreateRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        string raw = Base64Url.EncodeToString(bytes);

        return (raw, Hash(raw), _dateTime.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A plain SHA-256, not a password hash. The input is 256 bits of uniform randomness rather
    /// than a guessable secret, so there is nothing for a slow KDF to defend against — and refresh
    /// runs on every token rotation, where the cost would be real.
    /// </remarks>
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
