namespace MangaStore.Application.Features.Auth.Dtos;

/// <summary>Payload for requesting a password reset link.</summary>
/// <param name="Email">Address to send the reset link to, if an account exists for it.</param>
public sealed record ForgotPasswordRequest(string Email);
