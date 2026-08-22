namespace MangaStore.Infrastructure;

using System.Reflection;
using MangaStore.Application.Common.Events;
using MangaStore.Application.Common.Options;
using MangaStore.Domain.Interfaces;
using MangaStore.Infrastructure.HealthChecks;
using MangaStore.Infrastructure.Identity;
using MangaStore.Infrastructure.Persistence;
using MangaStore.Infrastructure.Persistence.Interceptors;
using MangaStore.Infrastructure.Persistence.Repositories;
using MangaStore.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Registers all Infrastructure-layer services into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>Adds EF Core, interceptors, repositories, the unit of work, and the domain event dispatcher.</summary>
    /// <remarks>
    /// SQL Server is the only supported provider. <c>Database:Provider</c> may be omitted or set to
    /// <c>SqlServer</c>; any other value throws at startup.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDateTime, SystemDateTime>();
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();

        services.AddPersistence(configuration);
        services.AddIdentityCore(configuration);

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        // Also matches *Service and *Sender so IdentityService, JwtTokenService and
        // LoggingEmailSender are discovered rather than registered by hand.
        services.Scan(scan => scan
            .FromAssemblies(Assembly.GetExecutingAssembly())
            .AddClasses(c => c.Where(t =>
                t.Name.EndsWith("Repository", StringComparison.Ordinal) ||
                t.Name.EndsWith("Service", StringComparison.Ordinal) ||
                t.Name.EndsWith("Sender", StringComparison.Ordinal)))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>(tags: [HealthCheckTags.Ready]);

        return services;
    }

    /// <summary>Registers ASP.NET Core Identity, the token/seed options, and the role seeder.</summary>
    /// <remarks>
    /// Uses <c>AddIdentityCore</c> rather than <c>AddIdentity</c>: this API authenticates with bearer
    /// tokens only, and <c>AddIdentity</c> would additionally wire up cookie authentication schemes
    /// that nothing here uses and that would override the default bearer scheme.
    /// </remarks>
    private static IServiceCollection AddIdentityCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SeedAdminOptions>()
            .Bind(configuration.GetSection(SeedAdminOptions.SectionName));

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;

            options.User.RequireUniqueEmail = true;

            // No confirmation flow exists yet; gating sign-in on it would lock everyone out.
            options.SignIn.RequireConfirmedEmail = false;
            options.SignIn.RequireConfirmedAccount = false;

            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddRoles<ApplicationRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<IdentitySeeder>();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        string? provider = configuration["Database:Provider"];

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            if (string.IsNullOrEmpty(provider) || string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported database provider '{provider}'. The only supported value is SqlServer.");
            }

            options.AddInterceptors(
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>());
        });

        return services;
    }
}
