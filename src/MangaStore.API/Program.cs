using MangaStore.API.Extensions;
using MangaStore.Application;
using MangaStore.Infrastructure;
using MangaStore.Infrastructure.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext());

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddAPI(builder.Configuration);

    var app = builder.Build();

    await app.SeedIdentityAsync();

    app.UseExceptionHandler();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging();

    app.UseCors();

    app.UseRateLimiter();

    app.UseResponseCompression();

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MangaStore API v1");
        options.RoutePrefix = "swagger";
    });

    app.MapScalarApiReference(options =>
    {
        options.WithTitle("MangaStore API")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
               .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
    });

    app.MapControllers();

    // Liveness answers "is this process alive?" and must never fail because a dependency is down —
    // otherwise the orchestrator restart-loops a healthy instance and deepens the outage.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

    // Readiness answers "should this instance receive traffic?" and does consult dependencies.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains(HealthCheckTags.Ready),
    });

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly.");
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposes the entry-point type for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program { }
