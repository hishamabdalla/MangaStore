namespace MangaStore.Infrastructure.HealthChecks;

/// <summary>Tags used to route registered health checks to the right endpoint.</summary>
public static class HealthCheckTags
{
    /// <summary>Marks a check as gating readiness — a dependency this instance needs before it should receive traffic.</summary>
    /// <remarks>
    /// Liveness intentionally runs no tagged checks. Anything added here can take an instance out of
    /// the load balancer, so tag only what genuinely makes the instance unable to serve requests.
    /// </remarks>
    public const string Ready = "ready";
}
