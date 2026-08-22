namespace MangaStore.IntegrationTests;

/// <summary>A host with the auth limiter low enough to trip deliberately.</summary>
/// <remarks>
/// Its own factory, because a fixed window's state lives for the lifetime of the host: sharing a
/// low limit with the other classes' factory would exhaust the window for tests that are not about
/// rate limiting at all.
/// </remarks>
public sealed class RateLimitedWebApplicationFactory : CustomWebApplicationFactory
{
    /// <summary>Requests allowed per window before the limiter rejects.</summary>
    public const int AuthPermitLimit = 3;

    /// <inheritdoc/>
    protected override void ConfigureAdditionalSettings(IDictionary<string, string?> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings["RateLimit:AuthPermitLimit"] = AuthPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings["RateLimit:WindowSeconds"] = "60";
    }
}
