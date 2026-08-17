namespace MangaStore.API.Options;

/// <summary>Fixed-window rate-limiting configuration bound from the <c>RateLimit</c> section of <c>appsettings.json</c>.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Name of the general-purpose fixed-window policy.</summary>
    public const string DefaultPolicy = "fixed";

    /// <summary>Name of the stricter policy applied to the authentication endpoints.</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Gets the length of the rate-limit window in seconds. Defaults to <c>60</c>.</summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Gets the maximum number of requests allowed per window. Defaults to <c>100</c>.</summary>
    public int PermitLimit { get; init; } = 100;

    /// <summary>Gets the per-window request limit for the auth endpoints. Kept low because these are the brute-force surface.</summary>
    public int AuthPermitLimit { get; init; } = 10;
}
