namespace MangaStore.Application.Common.Identity;

/// <summary>Short claim names used in issued access tokens.</summary>
/// <remarks>
/// The bearer handler must be configured with <c>MapInboundClaims = false</c> and these values as
/// its name and role claim types. Without that, the handler rewrites short names to the long
/// schema URIs and <c>[Authorize(Roles = ...)]</c> silently stops matching.
/// </remarks>
public static class AppClaimTypes
{
    /// <summary>Claim carrying the user identifier.</summary>
    public const string Subject = "sub";

    /// <summary>Claim carrying a role name. Emitted once per assigned role.</summary>
    public const string Role = "role";

    /// <summary>Claim carrying the user's email address.</summary>
    public const string Email = "email";
}
