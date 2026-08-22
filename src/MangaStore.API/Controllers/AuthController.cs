namespace MangaStore.API.Controllers;

using MangaStore.API.Controllers.Base;
using MangaStore.Application.Features.Auth;
using MangaStore.Application.Features.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Manages sign-up, sign-in, sessions, and password recovery.</summary>
/// <remarks>
/// <c>[AllowAnonymous]</c> is applied per action rather than to the controller: at controller level
/// it would override the <c>[Authorize]</c> on logout-all and change-password, leaving them open.
/// </remarks>
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Location returned with a 201: the session body is not addressable, but the account it created is.</summary>
    private const string CreatedAccountLocation = "/api/v1/users/me";

    private readonly IAuthService _authService;

    /// <summary>Initialises a new instance of <see cref="AuthController"/>.</summary>
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Creates an account and returns a signed-in session.</summary>
    /// <param name="request">Registration payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken ct) =>
        HandleCreated(await _authService.RegisterAsync(request, ct), CreatedAccountLocation);

    /// <summary>Exchanges credentials for an access token and a refresh token.</summary>
    /// <param name="request">Login payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken ct) =>
        HandleResult(await _authService.LoginAsync(request, ct));

    /// <summary>Rotates a session, revoking the presented refresh token and issuing a replacement pair.</summary>
    /// <param name="request">The refresh token to exchange.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RefreshAsync([FromBody] RefreshTokenRequest request, CancellationToken ct) =>
        HandleResult(await _authService.RefreshAsync(request, ct));

    /// <summary>Revokes a single refresh token.</summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> LogoutAsync([FromBody] RefreshTokenRequest request, CancellationToken ct) =>
        HandleDelete(await _authService.LogoutAsync(request, ct));

    /// <summary>Revokes every active session for the signed-in account.</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAllAsync(CancellationToken ct) =>
        HandleDelete(await _authService.LogoutAllAsync(ct));

    /// <summary>Emails a password reset link. Always succeeds, whether or not the address is registered.</summary>
    /// <param name="request">The address to send to.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordRequest request, CancellationToken ct) =>
        HandleDelete(await _authService.ForgotPasswordAsync(request, ct));

    /// <summary>Sets a new password using a token from a reset link, ending every existing session.</summary>
    /// <param name="request">Reset payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordRequest request, CancellationToken ct) =>
        HandleDelete(await _authService.ResetPasswordAsync(request, ct));

    /// <summary>Changes the signed-in account's password, ending every existing session.</summary>
    /// <param name="request">Change payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request, CancellationToken ct) =>
        HandleDelete(await _authService.ChangePasswordAsync(request, ct));
}
