namespace MangaStore.Application.Features.Auth.Dtos;

using MangaStore.Application.Features.Users.Dtos;

/// <summary>A newly issued session, returned by register, login, and refresh.</summary>
/// <param name="AccessToken">Signed JWT to send as a bearer token.</param>
/// <param name="AccessTokenExpiresAt">UTC instant the access token stops being accepted.</param>
/// <param name="RefreshToken">Single-use token that exchanges for the next session. Shown once and never retrievable again.</param>
/// <param name="User">The authenticated account.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    UserDto User);
