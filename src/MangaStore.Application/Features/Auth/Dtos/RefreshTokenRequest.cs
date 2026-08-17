namespace MangaStore.Application.Features.Auth.Dtos;

/// <summary>Payload for rotating a session, also used to revoke one on logout.</summary>
/// <param name="RefreshToken">The raw refresh token issued by the previous call.</param>
public sealed record RefreshTokenRequest(string RefreshToken);
