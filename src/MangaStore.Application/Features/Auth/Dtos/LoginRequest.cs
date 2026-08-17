namespace MangaStore.Application.Features.Auth.Dtos;

/// <summary>Payload for exchanging credentials for a session.</summary>
/// <param name="Email">Registered email address.</param>
/// <param name="Password">Plain-text password.</param>
public sealed record LoginRequest(string Email, string Password);
