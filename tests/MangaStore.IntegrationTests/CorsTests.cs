namespace MangaStore.IntegrationTests;

using Shouldly;
using Xunit;

/*
 * A regression guard on committed configuration rather than a failing test: the factory already
 * pins an explicit origin, so this passes today. It fails the moment appsettings.json goes back to
 * "*", which is what the phase repaired.
 */
public sealed class CorsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CorsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DisallowedOrigin_IsNotEchoed()
    {
        // A real route, so endpoint-aware CORS has something to match against.
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task AllowedOrigin_IsEchoed()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain("http://localhost:3000");
    }
}
