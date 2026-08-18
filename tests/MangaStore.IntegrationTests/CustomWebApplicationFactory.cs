namespace MangaStore.IntegrationTests;

using MangaStore.Application.Common.Email;
using MangaStore.Infrastructure.Persistence;
using MangaStore.Infrastructure.Persistence.Interceptors;
using MangaStore.IntegrationTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Bootstraps the full application with an in-memory SQLite database for integration testing.</summary>
/// <remarks>
/// Keeps one <see cref="SqliteConnection"/> open for the lifetime of the factory so that the
/// in-memory database is not destroyed between requests. The schema is created up front, before the
/// host starts, because start-up seeds roles and would otherwise query tables that do not exist yet.
/// One database is shared by every test in a class, so tests must use distinct email addresses.
/// </remarks>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Email address of the administrator seeded at start-up.</summary>
    public const string AdminEmail = "admin@mangastore.test";

    /// <summary>Password of the administrator seeded at start-up.</summary>
    public const string AdminPassword = "Admin123!";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>Gets the email sender the application resolved, for inspecting generated links.</summary>
    public CapturingEmailSender EmailSender { get; } = new();

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _connection.Open();
        CreateSchema();

        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // No Database:Provider — the app only knows SQL Server, and ConfigureServices
                // below replaces its DbContext registration wholesale with the SQLite one.
                ["Jwt:Secret"] = "test-only-secret-at-least-32-characters!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14",
                ["App:FrontendBaseUrl"] = "https://mangastore.test",
                ["Identity:SeedAdmin:Email"] = AdminEmail,
                ["Identity:SeedAdmin:Password"] = AdminPassword,
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove all existing DbContext-related registrations. We must also remove
            // IDbContextOptionsConfiguration<AppDbContext> because it holds the SQL Server
            // provider configuration — leaving it causes a "two providers registered" error
            // when the new SQLite registration is added on top.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(
                    sp.GetRequiredService<AuditInterceptor>(),
                    sp.GetRequiredService<SoftDeleteInterceptor>());
            });

            // Swap the logging sender for one that keeps the links so tests can follow them.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });

        // Makes the role-guarded test endpoint discoverable by MVC.
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(AdminOnlyController).Assembly));
    }

    /// <summary>Creates the schema on the shared connection before the host — and its role seeding — starts.</summary>
    private void CreateSchema()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(options);
        context.Database.EnsureCreated();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}
