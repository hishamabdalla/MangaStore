namespace MangaStore.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;

/// <summary>A named role that grants a set of permissions. Seeded from <c>Roles</c>.</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Initialises a new instance of <see cref="ApplicationRole"/>.</summary>
    public ApplicationRole() { }

    /// <summary>Initialises a new instance of <see cref="ApplicationRole"/> with the given name.</summary>
    /// <param name="roleName">The role name.</param>
    public ApplicationRole(string roleName) : base(roleName) { }
}
