namespace MangaStore.Application.Features.Auth;

using AutoMapper;
using MangaStore.Application.Common;
using MangaStore.Application.Common.Email;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Common.Options;
using MangaStore.Application.Common.Validation;
using MangaStore.Application.Features.Auth.Dtos;
using MangaStore.Application.Features.Users.Dtos;
using MangaStore.Domain.Features.Auth;
using MangaStore.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <inheritdoc cref="IAuthService"/>
public sealed partial class AuthService : IAuthService
{
    /// <summary>Returned for every failed sign-in so the response cannot distinguish a wrong password from an unknown account.</summary>
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private const string ErrorEntity = "Auth";

    private readonly IIdentityService _identityService;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly IValidationService _validationService;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _dateTime;
    private readonly IMapper _mapper;
    private readonly AppOptions _appOptions;
    private readonly ILogger<AuthService> _logger;

    /// <summary>Initialises a new instance of <see cref="AuthService"/>.</summary>
    public AuthService(
        IIdentityService identityService,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IValidationService validationService,
        ICurrentUser currentUser,
        IDateTime dateTime,
        IMapper mapper,
        IOptions<AppOptions> appOptions,
        ILogger<AuthService> logger)
    {
        ArgumentNullException.ThrowIfNull(appOptions);

        _identityService = identityService;
        _tokenService = tokenService;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _validationService = validationService;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _mapper = mapper;
        _appOptions = appOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return validation.Error;

        var created = await _identityService.CreateUserAsync(request.Email, request.Password, request.DisplayName, ct);
        if (created.IsFailure)
            return created.Error;

        var (response, _) = await IssueSessionAsync(created.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        Log.UserRegistered(_logger, created.Value.Id);
        return response;
    }

    /// <inheritdoc/>
    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return validation.Error;

        var user = await _identityService.FindByEmailAsync(request.Email, ct);
        if (user is null)
        {
            Log.LoginFailedUnknownEmail(_logger);
            return ResultError.Unauthorized(ErrorEntity, InvalidCredentialsMessage);
        }

        var check = await _identityService.CheckPasswordAsync(user.Id, request.Password, ct);
        switch (check)
        {
            case PasswordCheckResult.LockedOut:
                Log.LoginLockedOut(_logger, user.Id);
                return ResultError.Forbidden(ErrorEntity, "This account is temporarily locked after too many failed sign-in attempts. Try again later.");

            case PasswordCheckResult.InvalidCredentials:
                Log.LoginFailedBadPassword(_logger, user.Id);
                return ResultError.Unauthorized(ErrorEntity, InvalidCredentialsMessage);

            default:
                break;
        }

        var (response, _) = await IssueSessionAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        Log.LoginSucceeded(_logger, user.Id);
        return response;
    }

    /// <inheritdoc/>
    public async Task<Result<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return validation.Error;

        var now = _dateTime.UtcNow;
        string presentedHash = _tokenService.Hash(request.RefreshToken);
        var stored = await _refreshTokenRepository.GetByTokenHashAsync(presentedHash, ct);

        if (stored is null)
            return ResultError.Unauthorized(ErrorEntity, "Invalid refresh token.");

        // A token that exists but was already revoked means the chain leaked: whoever holds the
        // rotated-away copy is replaying it. Kill every session for the user rather than just this one.
        if (stored.RevokedAt is not null)
        {
            await RevokeAllSessionsAsync(stored.UserId, now, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            Log.RefreshTokenReuseDetected(_logger, stored.UserId);
            return ResultError.Unauthorized(ErrorEntity, "Invalid refresh token.");
        }

        if (!stored.IsActive(now))
            return ResultError.Unauthorized(ErrorEntity, "Invalid refresh token.");

        var user = await _identityService.FindByIdAsync(stored.UserId, ct);
        if (user is null)
            return ResultError.Unauthorized(ErrorEntity, "Invalid refresh token.");

        var (response, replacement) = await IssueSessionAsync(user, ct);
        stored.Revoke(now, replacement.TokenHash);
        await _unitOfWork.SaveChangesAsync(ct);

        Log.SessionRefreshed(_logger, user.Id);
        return response;
    }

    /// <inheritdoc/>
    public async Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return Result.Fail(validation.Error);

        var now = _dateTime.UtcNow;
        var stored = await _refreshTokenRepository.GetByTokenHashAsync(_tokenService.Hash(request.RefreshToken), ct);

        // An unknown token still reports success — logout must never double as a token oracle.
        if (stored is not null && stored.IsActive(now))
        {
            stored.Revoke(now);
            await _unitOfWork.SaveChangesAsync(ct);
            Log.SessionRevoked(_logger, stored.UserId);
        }

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> LogoutAllAsync(CancellationToken ct = default)
    {
        if (_currentUser.Id is not Guid userId)
            return Result.Fail(ResultError.Unauthorized(ErrorEntity, "Not authenticated."));

        var now = _dateTime.UtcNow;
        int revoked = await RevokeAllSessionsAsync(userId, now, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        Log.AllSessionsRevoked(_logger, userId, revoked);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return Result.Fail(validation.Error);

        var user = await _identityService.FindByEmailAsync(request.Email, ct);

        // Reporting success for an unregistered address is what stops this endpoint enumerating accounts.
        if (user is null)
        {
            Log.PasswordResetRequestedForUnknownEmail(_logger);
            return Result.Ok();
        }

        string token = await _identityService.GeneratePasswordResetTokenAsync(user.Id, ct);
        string link = BuildResetLink(user.Email, token);
        await _emailSender.SendPasswordResetAsync(user.Email, link, ct);

        Log.PasswordResetRequested(_logger, user.Id);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return Result.Fail(validation.Error);

        var user = await _identityService.FindByEmailAsync(request.Email, ct);

        // Same error for an unknown account as for a bad token, so neither reveals the other.
        if (user is null)
            return Result.Fail(ResultError.Validation("Invalid or expired password reset token."));

        var reset = await _identityService.ResetPasswordAsync(user.Id, request.Token, request.NewPassword, ct);
        if (reset.IsFailure)
            return reset;

        await RevokeAllSessionsAsync(user.Id, _dateTime.UtcNow, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        Log.PasswordReset(_logger, user.Id);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return Result.Fail(validation.Error);

        if (_currentUser.Id is not Guid userId)
            return Result.Fail(ResultError.Unauthorized(ErrorEntity, "Not authenticated."));

        var changed = await _identityService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);
        if (changed.IsFailure)
            return changed;

        await RevokeAllSessionsAsync(userId, _dateTime.UtcNow, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        Log.PasswordChanged(_logger, userId);
        return Result.Ok();
    }

    /// <summary>Builds a session and stages its refresh token for insertion. The caller commits the unit of work.</summary>
    private async Task<(AuthResponse Response, RefreshToken Token)> IssueSessionAsync(AppUserInfo user, CancellationToken ct)
    {
        var (accessToken, accessExpiresAt) = _tokenService.CreateAccessToken(user.Id, user.Email, user.Roles);
        var (rawRefresh, refreshHash, refreshExpiresAt) = _tokenService.CreateRefreshToken();

        var refreshToken = RefreshToken.Create(refreshHash, user.Id, refreshExpiresAt, _currentUser.IpAddress);
        await _refreshTokenRepository.AddAsync(refreshToken, ct);

        var response = new AuthResponse(accessToken, accessExpiresAt, rawRefresh, _mapper.Map<UserDto>(user));
        return (response, refreshToken);
    }

    private async Task<int> RevokeAllSessionsAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var active = await _refreshTokenRepository.GetActiveByUserAsync(userId, now, ct);
        foreach (var token in active)
        {
            token.Revoke(now);
        }

        return active.Count;
    }

    private string BuildResetLink(string email, string token) =>
        $"{_appOptions.FrontendBaseUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered.")]
        public static partial void UserRegistered(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in.")]
        public static partial void LoginSucceeded(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in attempted for an unregistered email address.")]
        public static partial void LoginFailedUnknownEmail(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed for user {UserId}: incorrect password.")]
        public static partial void LoginFailedBadPassword(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused for user {UserId}: account locked out.")]
        public static partial void LoginLockedOut(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Session refreshed for user {UserId}.")]
        public static partial void SessionRefreshed(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Session revoked for user {UserId}.")]
        public static partial void SessionRevoked(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Revoked {Count} session(s) for user {UserId}.")]
        public static partial void AllSessionsRevoked(ILogger logger, Guid userId, int count);

        [LoggerMessage(Level = LogLevel.Error, Message = "Refresh token reuse detected for user {UserId}. All sessions revoked.")]
        public static partial void RefreshTokenReuseDetected(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Password reset requested for user {UserId}.")]
        public static partial void PasswordResetRequested(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Password reset requested for an unregistered email address.")]
        public static partial void PasswordResetRequestedForUnknownEmail(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Password reset completed for user {UserId}.")]
        public static partial void PasswordReset(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Password changed for user {UserId}.")]
        public static partial void PasswordChanged(ILogger logger, Guid userId);
    }
}
