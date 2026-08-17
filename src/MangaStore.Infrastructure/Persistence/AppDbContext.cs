namespace MangaStore.Infrastructure.Persistence;

using System.Reflection;
using MangaStore.Domain.Features.Auth;
using MangaStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

/// <summary>The application's primary EF Core database context. Entity configurations are applied from the current assembly.</summary>
/// <remarks>
/// Derives from <see cref="IdentityDbContext{TUser, TRole, TKey}"/> so the ASP.NET Core Identity
/// tables live alongside the domain tables in one database and one unit of work.
/// </remarks>
public sealed class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    /// <summary>Initialises a new instance of <see cref="AppDbContext"/>.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTrackingWithIdentityResolution;
    }

    /// <summary>Gets the refresh tokens table.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Identity's own mappings must be applied before the assembly's configurations, so a
        // configuration in this assembly can refine them rather than be overwritten by them.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
