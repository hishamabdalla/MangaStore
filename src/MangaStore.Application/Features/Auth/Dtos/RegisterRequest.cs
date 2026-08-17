namespace MangaStore.Application.Features.Auth.Dtos;

/// <summary>Payload for creating a new account.</summary>
/// <param name="Email">Email address, which doubles as the login name.</param>
/// <param name="Password">Plain-text password, subject to the configured password policy.</param>
/// <param name="DisplayName">Name shown to other users.</param>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);
