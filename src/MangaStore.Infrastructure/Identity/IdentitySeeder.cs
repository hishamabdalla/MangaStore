namespace MangaStore.Infrastructure.Identity;

using MangaStore.Domain.Features.Identity;
using MangaStore.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Creates the well-known roles and, when configured, a bootstrap administrator.</summary>
/// <remarks>Idempotent: safe to run on every start.</remarks>
public sealed partial class IdentitySeeder
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDateTime _dateTime;
    private readonly SeedAdminOptions _adminOptions;
    private readonly ILogger<IdentitySeeder> _logger;

    /// <summary>Initialises a new instance of <see cref="IdentitySeeder"/>.</summary>
    public IdentitySeeder(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IDateTime dateTime,
        IOptions<SeedAdminOptions> adminOptions,
        ILogger<IdentitySeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(adminOptions);

        _roleManager = roleManager;
        _userManager = userManager;
        _dateTime = dateTime;
        _adminOptions = adminOptions.Value;
        _logger = logger;
    }

    /// <summary>Ensures every role in <see cref="Roles.All"/> exists, then seeds the administrator if configured.</summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        foreach (string role in Roles.All)
        {
            if (await _roleManager.RoleExistsAsync(role))
                continue;

            var created = await _roleManager.CreateAsync(new ApplicationRole(role));
            if (created.Succeeded)
            {
                Log.RoleCreated(_logger, role);
            }
            else
            {
                Log.RoleCreationFailed(_logger, role, Describe(created));
            }
        }

        await SeedAdminAsync();
    }

    private async Task SeedAdminAsync()
    {
        if (!_adminOptions.IsConfigured)
        {
            Log.AdminSeedSkipped(_logger);
            return;
        }

        if (await _userManager.FindByEmailAsync(_adminOptions.Email) is not null)
            return;

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = _adminOptions.Email,
            Email = _adminOptions.Email,
            DisplayName = _adminOptions.DisplayName,
            CreatedAt = _dateTime.UtcNow,
            EmailConfirmed = true,
        };

        var created = await _userManager.CreateAsync(admin, _adminOptions.Password);
        if (!created.Succeeded)
        {
            Log.AdminSeedFailed(_logger, Describe(created));
            return;
        }

        await _userManager.AddToRolesAsync(admin, [Roles.Admin, Roles.Customer]);
        Log.AdminSeeded(_logger, admin.Email);
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Seeded role {Role}.")]
        public static partial void RoleCreated(ILogger logger, string role);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to seed role {Role}: {Errors}")]
        public static partial void RoleCreationFailed(ILogger logger, string role, string errors);

        [LoggerMessage(Level = LogLevel.Information, Message = "Seeded administrator {Email}.")]
        public static partial void AdminSeeded(ILogger logger, string email);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to seed the administrator: {Errors}")]
        public static partial void AdminSeedFailed(ILogger logger, string errors);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No Identity:SeedAdmin configuration found; skipping administrator seeding.")]
        public static partial void AdminSeedSkipped(ILogger logger);
    }
}
