namespace MangaStore.Application.Common.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>JWT configuration bound from the <c>Jwt</c> section of <c>appsettings.json</c>.</summary>
/// <remarks>
/// Lives in Application rather than API because both sides need it: Infrastructure issues tokens
/// with these values and the API layer validates incoming tokens against them.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Gets the token issuer.</summary>
    [Required]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Gets the token audience.</summary>
    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>Gets the HMAC-SHA256 signing secret. Must be at least 32 characters.</summary>
    [Required, MinLength(32)]
    public string Secret { get; init; } = string.Empty;

    /// <summary>Gets the access token lifetime in minutes. Kept short because access tokens cannot be revoked.</summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Gets the refresh token lifetime in days.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 14;
}
