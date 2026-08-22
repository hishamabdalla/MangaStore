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
    /// <remarks>
    /// Money is <c>decimal(18,2)</c> everywhere by convention rather than per-property, so a new
    /// price column cannot silently inherit the provider's default precision and truncate.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateOnly>().HaveColumnType("date");

        base.ConfigureConventions(configurationBuilder);
    }

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
