namespace MangaStore.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using MangaStore.Application.Common.Localization;
using MangaStore.IntegrationTests.TestDoubles;
using Shouldly;
using Xunit;

public sealed class RequestLanguageTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RequestLanguageTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("ar-EG,ar;q=0.9,en;q=0.8", "ar")]
    [InlineData("ar", "ar")]
    [InlineData("AR-eg", "ar")]
    [InlineData("en-GB", "en")]
    [InlineData("fr", "en")]
    [InlineData("*", "en")]
    [InlineData("en;q=0.2,ar;q=0.9", "ar")]
    public async Task AcceptLanguage_ResolvesToSupportedCode(string header, string expected)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test-language");
        request.Headers.TryAddWithoutValidation("Accept-Language", header);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LanguageProbeResponse>())!.Code.ShouldBe(expected);
    }

    [Fact]
    public async Task NoAcceptLanguage_FallsBackToTheDefault()
    {
        var response = await _client.GetAsync("/api/v1/test-language");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LanguageProbeResponse>())!.Code.ShouldBe(SupportedLanguages.Default);
    }

    /// <summary>A header from a stranger must not become a 500.</summary>
    [Theory]
    [InlineData("))garbage;;q=zzz")]
    [InlineData(";;;")]
    [InlineData("en;q=")]
    public async Task MalformedAcceptLanguage_DoesNotThrow(string header)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test-language");
        request.Headers.TryAddWithoutValidation("Accept-Language", header);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LanguageProbeResponse>())!.Code.ShouldBe(SupportedLanguages.Default);
    }

    /// <summary>A quality of zero means "not acceptable", so it must never win.</summary>
    [Fact]
    public async Task ZeroQualityLanguage_IsNotSelected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test-language");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ar;q=0,en;q=0.5");

        var response = await _client.SendAsync(request);

        (await response.Content.ReadFromJsonAsync<LanguageProbeResponse>())!.Code.ShouldBe("en");
    }
}
