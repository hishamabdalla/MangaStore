namespace MangaStore.Application.Features.Auth;

using MangaStore.Application.Common;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Handles account creation, sign-in, session rotation, and password management.</summary>
public interface IAuthService
{
    /// <summary>Creates an account and signs it straight in.</summary>
    /// <param name="request">Registration payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new session, or a conflict error if the email is already registered.</returns>
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>Exchanges credentials for a new session.</summary>
    /// <param name="request">Login payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new session, an unauthorized error for bad credentials, or a forbidden error while locked out.</returns>
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Rotates a session, revoking the presented refresh token and issuing a replacement pair.</summary>
    /// <param name="request">The refresh token to exchange.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new session, or an unauthorized error if the token is unknown, expired, or already used.</returns>
    /// <remarks>Presenting an already-revoked token is treated as theft and revokes every session for that user.</remarks>
    Task<Result<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);

    /// <summary>Revokes a single refresh token. Succeeds even for an unknown token, so logout is never a probe.</summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default);

    /// <summary>Revokes every active refresh token for the signed-in account.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> LogoutAllAsync(CancellationToken ct = default);

    /// <summary>Emails a password reset link if the address is registered.</summary>
    /// <param name="request">The address to send to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success whether or not the account exists, so the endpoint cannot enumerate accounts.</returns>
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    /// <summary>Sets a new password using a token from a reset link, then revokes every existing session.</summary>
    /// <param name="request">Reset payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    /// <summary>Changes the signed-in account's password, then revokes every existing session.</summary>
    /// <param name="request">Change payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
}
