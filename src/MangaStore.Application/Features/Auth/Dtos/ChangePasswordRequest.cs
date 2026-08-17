namespace MangaStore.Application.Features.Auth.Dtos;

/// <summary>Payload for changing the password of the signed-in account.</summary>
/// <param name="CurrentPassword">The existing password, required as proof of possession.</param>
/// <param name="NewPassword">Replacement password, subject to the configured password policy.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
