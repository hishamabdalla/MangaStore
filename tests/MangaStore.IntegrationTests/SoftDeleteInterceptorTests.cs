namespace MangaStore.IntegrationTests;

using MangaStore.Domain.Features.Auth;
using MangaStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

/*
 * The interceptor has never had a test, and every entity the catalogue phases add inherits its
 * behaviour: a DELETE that is actually destructive would take order history, stock ledgers and
 * gift-card codes with it. Cheap to pin now, expensive to discover later.
 */
public sealed class SoftDeleteInterceptorTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SoftDeleteInterceptorTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Each token needs its own hash — the column carries a unique index.</summary>
    private static string UniqueHash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private async Task<Guid> AddTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var token = RefreshToken.Create(UniqueHash(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), "127.0.0.1");
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        return token.Id;
    }

    private async Task RemoveAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // AsTracking because the context is NoTrackingWithIdentityResolution globally.
        var token = await db.RefreshTokens.AsTracking().FirstAsync(t => t.Id == id);
        db.RefreshTokens.Remove(token);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Remove_MarksTheRowDeletedRatherThanDeletingIt()
    {
        var id = await AddTokenAsync();

        await RemoveAsync(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        stored.ShouldNotBeNull();
        stored.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Remove_HidesTheRowFromOrdinaryQueries()
    {
        var id = await AddTokenAsync();

        await RemoveAsync(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == id)).ShouldBeNull();
    }

    [Fact]
    public async Task Remove_LeavesOtherRowsAlone()
    {
        var doomed = await AddTokenAsync();
        var survivor = await AddTokenAsync();

        await RemoveAsync(doomed);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == survivor)).ShouldNotBeNull();
    }

    /// <summary>The audit interceptor runs on the same save, so a soft delete is a modification.</summary>
    [Fact]
    public async Task Remove_StampsUpdatedAt()
    {
        var id = await AddTokenAsync();

        await RemoveAsync(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.RefreshTokens.IgnoreQueryFilters().FirstAsync(t => t.Id == id);
        stored.UpdatedAt.ShouldNotBeNull();
    }
}
