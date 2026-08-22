namespace MangaStore.Infrastructure.Http;

using Microsoft.Extensions.Http.Resilience;

/// <summary>Shared resilience profiles applied to outbound typed <see cref="HttpClient"/> instances.</summary>
/// <remarks>
/// <para>
/// Two profiles exist because <b>retries are only safe on idempotent operations</b>. Reading a
/// spreadsheet range can be retried freely; creating a payment session cannot — a retry there is a
/// duplicate charge against a real customer.
/// </para>
/// <para>
/// Pick <see cref="ConfigureReadOnlyExternal"/> for reads and <see cref="ConfigureNonIdempotentExternal"/>
/// for anything that creates or mutates state on the far side. When in doubt, assume non-idempotent:
/// an unnecessary failure is recoverable, a duplicate charge is not.
/// </para>
/// </remarks>
public static class ResilienceDefaults
{
    /// <summary>Logical name of the typed client used for read-only external calls.</summary>
    public const string ReadOnlyExternalClient = "read-only-external";

    /// <summary>Logical name of the typed client used for non-idempotent external calls.</summary>
    public const string NonIdempotentExternalClient = "non-idempotent-external";

    /// <summary>Total time budget for a read-only call, including all retry attempts.</summary>
    private static readonly TimeSpan ReadOnlyTotalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Total time budget for a non-idempotent call. Longer, because it gets exactly one attempt.</summary>
    private static readonly TimeSpan NonIdempotentTotalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Configures retries, timeouts, and a circuit breaker for idempotent read-only calls.</summary>
    /// <param name="options">The standard resilience options supplied by <c>AddStandardResilienceHandler</c>.</param>
    public static void ConfigureReadOnlyExternal(HttpStandardResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.TotalRequestTimeout.Timeout = ReadOnlyTotalTimeout;

        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;

        // Must be shorter than the total budget, or the first attempt consumes it entirely.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    }

    /// <summary>Configures timeouts and a circuit breaker, with retries <b>disabled</b>, for state-changing calls.</summary>
    /// <param name="options">The standard resilience options supplied by <c>AddStandardResilienceHandler</c>.</param>
    /// <remarks>
    /// Retries are off by design. Idempotency for these calls comes from an application-level
    /// idempotency key on the request, never from transport-level replay.
    /// </remarks>
    public static void ConfigureNonIdempotentExternal(HttpStandardResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.TotalRequestTimeout.Timeout = NonIdempotentTotalTimeout;

        options.Retry.MaxRetryAttempts = 0;

        // Must stay strictly below the total budget, and the sampling window at least twice this.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(25);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
    }
}
