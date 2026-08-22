namespace MangaStore.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MangaStore.Application.Features.Auth.Dtos;
using MangaStore.Domain.Features.Identity;
using Shouldly;
using Xunit;

/*
 * Asserted against raw JSON rather than a deserialised DTO. Round-tripping through the same
 * converters that produced the payload would agree with itself no matter what it wrote; the
 * storefront reads the bytes.
 */
public sealed class SerializationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SerializationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@mangastore.test";

    private async Task<JsonElement> GetProbeAsync()
    {
        var response = await _client.GetAsync("/api/v1/test-serialization");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>An integer would render in the client's translation lookup as <c>stock.2</c>.</summary>
    [Fact]
    public async Task Enum_SerializesAsCamelCaseString()
    {
        var probe = await GetProbeAsync();

        probe.GetProperty("status").ValueKind.ShouldBe(JsonValueKind.String);
        probe.GetProperty("status").GetString().ShouldBe("preOrder");
    }

    /// <summary>Without the designator the browser reads the value as local time and the date can slip a day.</summary>
    [Fact]
    public async Task DateTime_CarriesZDesignator()
    {
        var probe = await GetProbeAsync();

        string timestamp = probe.GetProperty("timestamp").GetString()!;

        timestamp.ShouldEndWith("Z");
        timestamp.ShouldStartWith("2026-05-14T22:04:21");
    }

    [Fact]
    public async Task NullableDateTime_WhenAbsent_SerializesAsNull()
    {
        var probe = await GetProbeAsync();

        probe.GetProperty("optionalTimestamp").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>A date-only value given a time is re-localised by the client and lands a day early.</summary>
    [Fact]
    public async Task DateOnly_StaysABareCalendarDate()
    {
        var probe = await GetProbeAsync();

        string releasedOn = probe.GetProperty("releasedOn").GetString()!;

        releasedOn.ShouldBe("2026-05-14");
        releasedOn.Length.ShouldBe(10);
        releasedOn.ShouldNotContain("T");
        releasedOn.ShouldNotContain("Z");
    }

    /// <summary>A real endpoint, not the probe: this is the field the storefront's <c>parseApiDate</c> exists for.</summary>
    [Fact]
    public async Task UserCreatedAt_CarriesZDesignator()
    {
        var register = await _client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(UniqueEmail("stamped"), "Password1", "Test Reader"));
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = JsonDocument.Parse(await register.Content.ReadAsStringAsync()).RootElement;

        body.GetProperty("accessTokenExpiresAt").GetString()!.ShouldEndWith("Z");
        body.GetProperty("user").GetProperty("createdAt").GetString()!.ShouldEndWith("Z");
    }

    /*
     * Roles are const strings, not an enum, so JsonStringEnumConverter cannot reach them — but the
     * storefront matches 'Customer' and 'Admin' exactly, and a lower-cased array would fail every
     * role guard silently. This pins that the camelCase converter left them alone.
     */
    [Fact]
    public async Task RolesArray_StaysPascalCase()
    {
        var session = await _client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(UniqueEmail("roled"), "Password1", "Test Reader"));
        var registered = (await session.Content.ReadFromJsonAsync<AuthResponse>())!;

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);

        var me = await _client.SendAsync(request);
        var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement;

        string?[] roles = body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        roles.ShouldBe([Roles.Customer]);
        roles.ShouldContain("Customer");
    }
}
