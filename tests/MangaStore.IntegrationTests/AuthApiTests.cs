namespace MangaStore.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Web;
using MangaStore.Application.Features.Auth.Dtos;
using MangaStore.Application.Features.Users.Dtos;
using MangaStore.Domain.Features.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

public sealed class AuthApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Password1";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Each test uses its own address; the database is shared across the class.</summary>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@mangastore.test";

    private async Task<AuthResponse> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(email, Password, "Test Reader"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task Register_CreatesAnAccountAndReturnsAUsableSession()
    {
        string email = UniqueEmail("newcomer");

        var session = await RegisterAsync(email);

        session.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        session.User.Email.ShouldBe(email);
        session.User.Roles.ShouldBe([Roles.Customer]);

        // The access token works immediately — there is no confirmation step to clear.
        var me = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/users/me", session.AccessToken));
        me.StatusCode.ShouldBe(HttpStatusCode.OK);

        var profile = await me.Content.ReadFromJsonAsync<UserDto>();
        profile!.Email.ShouldBe(email);
        profile.DisplayName.ShouldBe("Test Reader");
    }

    [Fact]
    public async Task Register_WithAnAlreadyRegisteredEmail_Returns409()
    {
        string email = UniqueEmail("duplicate");
        await RegisterAsync(email);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(email, Password, "Impostor"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Register_WithAWeakPassword_Returns422()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(UniqueEmail("weak"), "short", "Test Reader"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_Returns401AndRevealsNothing()
    {
        string email = UniqueEmail("wrongpass");
        await RegisterAsync(email);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(email, "NotThePassw0rd"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_ReturnsTheSameResponseAsAWrongPassword()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(UniqueEmail("ghost"), Password));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAToken_Returns401()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesTheSessionAndKillsTheOldToken()
    {
        var session = await RegisterAsync(UniqueEmail("rotator"));

        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshTokenRequest(session.RefreshToken));

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var next = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;
        next.RefreshToken.ShouldNotBe(session.RefreshToken);

        // The rotated-away token must not work a second time.
        var replay = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshTokenRequest(session.RefreshToken));

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReplayingARevokedToken_AlsoKillsTheTokenThatReplacedIt()
    {
        var session = await RegisterAsync(UniqueEmail("thief"));

        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshTokenRequest(session.RefreshToken));
        var current = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;

        // Replaying the old token signals theft, so the whole chain is revoked...
        var replay = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshTokenRequest(session.RefreshToken));
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...including the legitimate holder's newest token.
        var afterBreach = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshTokenRequest(current.RefreshToken));
        afterBreach.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var session = await RegisterAsync(UniqueEmail("leaver"));

        var logout = await _client.PostAsJsonAsync(
            "/api/v1/auth/logout", new RefreshTokenRequest(session.RefreshToken));
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshTokenRequest(session.RefreshToken));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForgotPassword_ForAnUnknownEmail_Returns204AndSendsNothing()
    {
        string email = UniqueEmail("never-registered");

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _factory.EmailSender.PasswordResets.ShouldNotContain(r => r.Email == email);
    }

    [Fact]
    public async Task PasswordReset_LetsTheUserSignInWithTheNewPasswordAndNotTheOld()
    {
        const string NewPassword = "Rebuilt2026";
        string email = UniqueEmail("forgetful");
        await RegisterAsync(email);

        var requested = await _client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        requested.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        string token = ExtractResetToken(_factory.EmailSender.LatestResetLinkFor(email));

        var reset = await _client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new ResetPasswordRequest(email, token, NewPassword));
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var withOld = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        withOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var withNew = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, NewPassword));
        withNew.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminEndpoint_AsACustomer_Returns403WithAProblemDetailsBody()
    {
        var session = await RegisterAsync(UniqueEmail("customer"));

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/test-admin", session.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The default middleware would produce an empty body; the project requires ProblemDetails.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status403Forbidden);
        problem.Title.ShouldBe("Auth.Forbidden");
    }

    [Fact]
    public async Task AdminEndpoint_AsTheSeededAdmin_Returns200()
    {
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(CustomWebApplicationFactory.AdminEmail, CustomWebApplicationFactory.AdminPassword));

        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        session.User.Roles.ShouldContain(Roles.Admin);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/test-admin", session.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateProfile_ChangesTheDisplayName()
    {
        var session = await RegisterAsync(UniqueEmail("renamer"));

        var request = Authorized(HttpMethod.Put, "/api/v1/users/me", session.AccessToken);
        request.Content = JsonContent.Create(new UpdateProfileRequest("Renamed Reader"));

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        updated!.DisplayName.ShouldBe("Renamed Reader");
    }

    private static string ExtractResetToken(string link) =>
        HttpUtility.ParseQueryString(new Uri(link).Query)["token"]
        ?? throw new InvalidOperationException($"No token in reset link: {link}");
}
