namespace MangaStore.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;

/// <summary>The persisted user account.</summary>
/// <remarks>
/// Deliberately confined to Infrastructure: it inherits an EF-aware Identity type, so exposing it
/// upward would drag ASP.NET Core Identity into Application and Domain. The Application layer sees
/// <c>AppUserInfo</c> instead.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Gets or sets the name shown to other users.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC instant the account was created.</summary>
    public DateTime CreatedAt { get; set; }
}
