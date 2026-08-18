namespace MangaStore.IntegrationTests;

using System.Net;
using Shouldly;
using Xunit;

/// <summary>Covers the liveness/readiness split introduced in Phase 01.</summary>
public sealed class HealthCheckTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthCheckTests(CustomWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task BothEndpoints_ReportHealthy(string url)
    {
        using var response = await _client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task TheCombinedEndpoint_IsGone()
    {
        // The split replaced /health rather than adding to it. If this starts passing as 200 again,
        // something re-registered the combined endpoint and probes may be watching the wrong thing.
        using var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
