namespace MangaStore.API.Extensions;

using MangaStore.Infrastructure.Identity;

/// <summary>Start-up steps that run against the built application.</summary>
public static class WebApplicationExtensions
{
    /// <summary>Creates the well-known roles and the configured bootstrap administrator.</summary>
    /// <remarks>
    /// Runs on every start and is idempotent. It does not create or migrate the schema — that stays
    /// an explicit <c>dotnet ef database update</c> step so a deployment never migrates by surprise.
    /// </remarks>
    /// <param name="app">The built application.</param>
    public static async Task SeedIdentityAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();
    }
}
