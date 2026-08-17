namespace MangaStore.UnitTests.Features.Auth;

using AutoMapper;
using MangaStore.Application.Common;
using MangaStore.Application.Common.Email;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Common.Options;
using MangaStore.Application.Common.Validation;
using MangaStore.Application.Features.Auth;
using MangaStore.Application.Features.Auth.Dtos;
using MangaStore.Application.Features.Users.Profiles;
using MangaStore.Domain.Features.Auth;
using MangaStore.Domain.Features.Identity;
using MangaStore.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

public sealed class AuthServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IValidationService _validationService = Substitute.For<IValidationService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTime _dateTime = Substitute.For<IDateTime>();

    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _dateTime.UtcNow.Returns(Now);

        // Every request type validates cleanly unless a test says otherwise.
        _validationService
            .ValidateAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        _tokenService.CreateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(("access-token", Now.AddMinutes(15)));
        _tokenService.CreateRefreshToken()
            .Returns(("raw-refresh", "hash-refresh", Now.AddDays(14)));
        _tokenService.Hash(Arg.Any<string>()).Returns(call => $"hashed:{call.Arg<string>()}");

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<UserProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

        _sut = new AuthService(
            _identityService,
            _tokenService,
            _refreshTokens,
            _unitOfWork,
            _emailSender,
            _validationService,
            _currentUser,
            _dateTime,
            mapper,
            Options.Create(new AppOptions { FrontendBaseUrl = "https://mangastore.test" }),
            NullLogger<AuthService>.Instance);
    }

    private static AppUserInfo User(params string[] roles) =>
        new(UserId, "reader@mangastore.test", "Reader", roles.Length == 0 ? [Roles.Customer] : roles, Now);

    private static RefreshToken StoredToken(DateTime expiresAt, Guid? userId = null) =>
        RefreshToken.Create("stored-hash", userId ?? UserId, expiresAt, "127.0.0.1");

    // ---- register ----

    [Fact]
    public async Task RegisterAsync_WithNewEmail_ReturnsSessionAndPersistsRefreshToken()
    {
        _identityService
            .CreateUserAsync("reader@mangastore.test", "Password1", "Reader", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(User()));

        var result = await _sut.RegisterAsync(new RegisterRequest("reader@mangastore.test", "Password1", "Reader"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-token");
        result.Value.RefreshToken.ShouldBe("raw-refresh");
        result.Value.User.Roles.ShouldBe([Roles.Customer]);

        await _refreshTokens.Received(1).AddAsync(
            Arg.Is<RefreshToken>(t => t.TokenHash == "hash-refresh" && t.UserId == UserId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ReturnsConflictAndIssuesNoTokens()
    {
        _identityService
            .CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AppUserInfo>(ResultError.Conflict("User", "Already exists.")));

        var result = await _sut.RegisterAsync(new RegisterRequest("taken@mangastore.test", "Password1", "Reader"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Conflict);

        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- login ----

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSession()
    {
        _identityService.FindByEmailAsync("reader@mangastore.test", Arg.Any<CancellationToken>()).Returns(User());
        _identityService.CheckPasswordAsync(UserId, "Password1", Arg.Any<CancellationToken>())
            .Returns(PasswordCheckResult.Success);

        var result = await _sut.LoginAsync(new LoginRequest("reader@mangastore.test", "Password1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-token");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsUnauthorizedWithoutRevealingTheAccountIsMissing()
    {
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AppUserInfo?)null);

        var result = await _sut.LoginAsync(new LoginRequest("nobody@mangastore.test", "Password1"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Unauthorized);
        result.Error.Message.ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsTheSameMessageAsAnUnknownEmail()
    {
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(User());
        _identityService.CheckPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PasswordCheckResult.InvalidCredentials);

        var result = await _sut.LoginAsync(new LoginRequest("reader@mangastore.test", "WrongPass1"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Unauthorized);
        result.Error.Message.ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task LoginAsync_WhenLockedOut_ReturnsForbidden()
    {
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(User());
        _identityService.CheckPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PasswordCheckResult.LockedOut);

        var result = await _sut.LoginAsync(new LoginRequest("reader@mangastore.test", "Password1"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Forbidden);
    }

    // ---- refresh ----

    [Fact]
    public async Task RefreshAsync_WithActiveToken_RotatesAndRevokesTheOldToken()
    {
        var stored = StoredToken(Now.AddDays(7));
        _refreshTokens.GetByTokenHashAsync("hashed:raw", Arg.Any<CancellationToken>()).Returns(stored);
        _identityService.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(User());

        var result = await _sut.RefreshAsync(new RefreshTokenRequest("raw"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RefreshToken.ShouldBe("raw-refresh");

        stored.RevokedAt.ShouldBe(Now);
        stored.ReplacedByTokenHash.ShouldBe("hash-refresh");
        await _refreshTokens.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_WithRevokedToken_RevokesEveryActiveSessionForThatUser()
    {
        var replayed = StoredToken(Now.AddDays(7));
        replayed.Revoke(Now.AddDays(-1));

        var otherSessions = new[] { StoredToken(Now.AddDays(3)), StoredToken(Now.AddDays(5)) };

        _refreshTokens.GetByTokenHashAsync("hashed:raw", Arg.Any<CancellationToken>()).Returns(replayed);
        _refreshTokens.GetActiveByUserAsync(UserId, Now, Arg.Any<CancellationToken>()).Returns(otherSessions);

        var result = await _sut.RefreshAsync(new RefreshTokenRequest("raw"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Unauthorized);

        otherSessions.ShouldAllBe(t => t.RevokedAt == Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // No replacement session is handed out to whoever replayed the token.
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_WithExpiredToken_ReturnsUnauthorized()
    {
        _refreshTokens.GetByTokenHashAsync("hashed:raw", Arg.Any<CancellationToken>())
            .Returns(StoredToken(Now.AddSeconds(-1)));

        var result = await _sut.RefreshAsync(new RefreshTokenRequest("raw"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Unauthorized);
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_WithUnknownToken_ReturnsUnauthorized()
    {
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var result = await _sut.RefreshAsync(new RefreshTokenRequest("raw"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Unauthorized);
    }

    // ---- logout ----

    [Fact]
    public async Task LogoutAsync_WithUnknownToken_StillSucceedsSoItCannotProbeForValidTokens()
    {
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var result = await _sut.LogoutAsync(new RefreshTokenRequest("raw"));

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutAsync_WithActiveToken_RevokesIt()
    {
        var stored = StoredToken(Now.AddDays(7));
        _refreshTokens.GetByTokenHashAsync("hashed:raw", Arg.Any<CancellationToken>()).Returns(stored);

        var result = await _sut.LogoutAsync(new RefreshTokenRequest("raw"));

        result.IsSuccess.ShouldBeTrue();
        stored.RevokedAt.ShouldBe(Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutAllAsync_WhenAnonymous_ReturnsUnauthorized()
    {
        _currentUser.Id.Returns((Guid?)null);

        var result = await _sut.LogoutAllAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Unauthorized);
    }

    // ---- password recovery ----

    [Fact]
    public async Task ForgotPasswordAsync_WithUnknownEmail_SucceedsWithoutSendingAnything()
    {
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AppUserInfo?)null);

        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("nobody@mangastore.test"));

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.DidNotReceive().SendPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithKnownEmail_SendsALinkCarryingTheToken()
    {
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(User());
        _identityService.GeneratePasswordResetTokenAsync(UserId, Arg.Any<CancellationToken>())
            .Returns("reset-token");

        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("reader@mangastore.test"));

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.Received(1).SendPasswordResetAsync(
            "reader@mangastore.test",
            Arg.Is<string>(link =>
                link.StartsWith("https://mangastore.test/reset-password?", StringComparison.Ordinal) &&
                link.Contains("token=reset-token", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPasswordAsync_OnSuccess_RevokesEveryExistingSession()
    {
        var sessions = new[] { StoredToken(Now.AddDays(3)) };
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(User());
        _identityService.ResetPasswordAsync(UserId, "token", "NewPassw0rd", Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _refreshTokens.GetActiveByUserAsync(UserId, Now, Arg.Any<CancellationToken>()).Returns(sessions);

        var result = await _sut.ResetPasswordAsync(
            new ResetPasswordRequest("reader@mangastore.test", "token", "NewPassw0rd"));

        result.IsSuccess.ShouldBeTrue();
        sessions.ShouldAllBe(t => t.RevokedAt == Now);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithUnknownEmail_ReportsTheSameErrorAsABadToken()
    {
        _identityService.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AppUserInfo?)null);

        var result = await _sut.ResetPasswordAsync(
            new ResetPasswordRequest("nobody@mangastore.test", "token", "NewPassw0rd"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldBe("Invalid or expired password reset token.");
    }

    [Fact]
    public async Task ChangePasswordAsync_OnSuccess_RevokesEveryExistingSession()
    {
        var sessions = new[] { StoredToken(Now.AddDays(3)) };
        _currentUser.Id.Returns(UserId);
        _identityService.ChangePasswordAsync(UserId, "Password1", "NewPassw0rd", Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _refreshTokens.GetActiveByUserAsync(UserId, Now, Arg.Any<CancellationToken>()).Returns(sessions);

        var result = await _sut.ChangePasswordAsync(new ChangePasswordRequest("Password1", "NewPassw0rd"));

        result.IsSuccess.ShouldBeTrue();
        sessions.ShouldAllBe(t => t.RevokedAt == Now);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenValidationFails_ShortCircuitsBeforeTouchingIdentity()
    {
        _validationService
            .ValidateAsync(Arg.Any<ChangePasswordRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(ResultError.Validation("New password is too short.")));

        var result = await _sut.ChangePasswordAsync(new ChangePasswordRequest("Password1", "short"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Validation);
        await _identityService.DidNotReceive().ChangePasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
