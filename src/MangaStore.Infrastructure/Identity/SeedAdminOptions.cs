namespace MangaStore.Infrastructure.Identity;

/// <summary>Optional bootstrap administrator, bound from the <c>Identity:SeedAdmin</c> configuration section.</summary>
/// <remarks>
/// Leave <see cref="Email"/> or <see cref="Password"/> empty to skip admin seeding entirely. Supply
/// them through user secrets or environment variables — never the committed <c>appsettings.json</c>.
/// </remarks>
public sealed class SeedAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Identity:SeedAdmin";

    /// <summary>Gets the administrator's email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the administrator's initial password.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Gets the administrator's display name.</summary>
    public string DisplayName { get; init; } = "Administrator";

    /// <summary>Gets a value indicating whether both required values are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}
