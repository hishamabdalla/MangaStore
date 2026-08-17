namespace MangaStore.Application.Features.Users.Dtos;

/// <summary>Payload for updating the signed-in account's profile.</summary>
/// <param name="DisplayName">Replacement name shown to other users.</param>
public sealed record UpdateProfileRequest(string DisplayName);
