namespace MangaStore.Application.Features.Auth.Dtos;

/// <summary>Payload for completing a password reset.</summary>
/// <param name="Email">Address the reset was requested for.</param>
/// <param name="Token">Base64url-encoded token from the reset link.</param>
/// <param name="NewPassword">Replacement password, subject to the configured password policy.</param>
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
