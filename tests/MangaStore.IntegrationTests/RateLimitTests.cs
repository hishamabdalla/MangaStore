namespace MangaStore.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using MangaStore.Application.Features.Auth.Dtos;
using Shouldly;
using Xunit;

/*
 * Uses its own host: the fixed window's state lives for the lifetime of the factory, so a limit low
 * enough to trip would exhaust the window for every other class sharing it.
 *
 * TestServer leaves RemoteIpAddress null, so all of these land in the single "unknown" partition —
 * which is exactly what makes them countable here, and exactly why the shared factory raises the
 * limit to 1000 instead.
 */
public sealed class RateLimitTests : IClassFixture<RateLimitedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RateLimitTests(RateLimitedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuthEndpoint_BeyondLimit_Returns429()
    {
        var request = new LoginRequest($"limiter-{Guid.NewGuid():N}@mangastore.test", "Password1");
        var statuses = new List<HttpStatusCode>();

        for (int attempt = 0; attempt < RateLimitedWebApplicationFactory.AuthPermitLimit + 2; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);

        // The rejection comes after the permitted requests, not instead of them.
        statuses.Take(RateLimitedWebApplicationFactory.AuthPermitLimit)
            .ShouldAllBe(status => status != HttpStatusCode.TooManyRequests);
    }

    /// <summary>The limiter guards the auth surface; nothing else is throttled at this phase.</summary>
    [Fact]
    public async Task NonAuthEndpoint_IsNotThrottled()
    {
        for (int attempt = 0; attempt < RateLimitedWebApplicationFactory.AuthPermitLimit + 5; attempt++)
        {
            var response = await _client.GetAsync("/health/live");
            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
    }
}
