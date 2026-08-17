namespace MangaStore.Application.Common.Identity;

/// <summary>Layer-neutral snapshot of a user account, returned by <see cref="IIdentityService"/>.</summary>
/// <remarks>
/// Exists so the Application layer never handles an ASP.NET Core Identity type directly. Roles are
/// always populated: every caller needs them, either to stamp role claims onto an access token or
/// to render the account.
/// </remarks>
/// <param name="Id">Unique identifier.</param>
/// <param name="Email">Email address, which doubles as the login name.</param>
/// <param name="DisplayName">Name shown to other users.</param>
/// <param name="Roles">Role names assigned to the account.</param>
/// <param name="CreatedAt">UTC instant the account was created.</param>
public sealed record AppUserInfo(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt);
