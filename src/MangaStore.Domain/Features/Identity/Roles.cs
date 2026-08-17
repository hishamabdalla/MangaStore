namespace MangaStore.Domain.Features.Identity;

/// <summary>Well-known role names. Referenced by role seeding, JWT claims, and <c>[Authorize]</c> attributes.</summary>
public static class Roles
{
    /// <summary>Default role assigned to every self-registered account.</summary>
    public const string Customer = nameof(Customer);

    /// <summary>Full administrative access to catalogue and user management.</summary>
    public const string Admin = nameof(Admin);

    /// <summary>Gets every role name, used by the seeder to create the full set.</summary>
    public static IReadOnlyList<string> All { get; } = [Customer, Admin];
}
