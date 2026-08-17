namespace MangaStore.Infrastructure.Identity;

using System.Buffers.Text;
using System.Text;
using MangaStore.Application.Common;
using MangaStore.Application.Common.Identity;
using MangaStore.Domain.Features.Identity;
using MangaStore.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;

/// <inheritdoc cref="IIdentityService"/>
public sealed class IdentityService : IIdentityService
{
    private const string ErrorEntity = "User";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDateTime _dateTime;

    /// <summary>Initialises a new instance of <see cref="IdentityService"/>.</summary>
    public IdentityService(UserManager<ApplicationUser> userManager, IDateTime dateTime)
    {
        _userManager = userManager;
        _dateTime = dateTime;
    }

    /// <inheritdoc/>
    public async Task<Result<AppUserInfo>> CreateUserAsync(string email, string password, string displayName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedAt = _dateTime.UtcNow,

            // No confirmation flow exists yet, so the flag is set once at creation rather than
            // left false and used as a gate nothing can ever clear.
            EmailConfirmed = true,
        };

        var created = await _userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return IsDuplicate(created)
                ? ResultError.Conflict(ErrorEntity, "An account with that email address already exists.")
                : ResultError.Validation(Describe(created));
        }

        var roleAssigned = await _userManager.AddToRoleAsync(user, Roles.Customer);
        if (!roleAssigned.Succeeded)
            return ResultError.Failure(ErrorEntity, Describe(roleAssigned));

        return ToInfo(user, [Roles.Customer]);
    }

    /// <inheritdoc/>
    public async Task<AppUserInfo?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByEmailAsync(email);
        return user is null ? null : ToInfo(user, await _userManager.GetRolesAsync(user));
    }

    /// <inheritdoc/>
    public async Task<AppUserInfo?> FindByIdAsync(Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : ToInfo(user, await _userManager.GetRolesAsync(user));
    }

    /// <inheritdoc/>
    public async Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return PasswordCheckResult.InvalidCredentials;

        if (await _userManager.IsLockedOutAsync(user))
            return PasswordCheckResult.LockedOut;

        if (await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.ResetAccessFailedCountAsync(user);
            return PasswordCheckResult.Success;
        }

        // Records the failure and locks the account once the threshold is crossed.
        await _userManager.AccessFailedAsync(user);
        return await _userManager.IsLockedOutAsync(user)
            ? PasswordCheckResult.LockedOut
            : PasswordCheckResult.InvalidCredentials;
    }

    /// <inheritdoc/>
    public async Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return string.Empty;

        string token = await _userManager.GeneratePasswordResetTokenAsync(user);
        return Encode(token);
    }

    /// <inheritdoc/>
    public async Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Fail(ResultError.Validation(InvalidResetToken));

        if (!TryDecode(token, out string decoded))
            return Result.Fail(ResultError.Validation(InvalidResetToken));

        var reset = await _userManager.ResetPasswordAsync(user, decoded, newPassword);
        return reset.Succeeded
            ? Result.Ok()
            : Result.Fail(ResultError.Validation(Describe(reset)));
    }

    /// <inheritdoc/>
    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Fail(ResultError.NotFound(ErrorEntity, "This account no longer exists."));

        var changed = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (changed.Succeeded)
            return Result.Ok();

        return IsIncorrectPassword(changed)
            ? Result.Fail(ResultError.Unauthorized(ErrorEntity, "The current password is incorrect."))
            : Result.Fail(ResultError.Validation(Describe(changed)));
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Fail(ResultError.NotFound(ErrorEntity, "This account no longer exists."));

        user.DisplayName = displayName;

        var updated = await _userManager.UpdateAsync(user);
        return updated.Succeeded
            ? Result.Ok()
            : Result.Fail(ResultError.Validation(Describe(updated)));
    }

    private const string InvalidResetToken = "Invalid or expired password reset token.";

    private static AppUserInfo ToInfo(ApplicationUser user, IList<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, [.. roles], user.CreatedAt);

    private static bool IsDuplicate(IdentityResult result) =>
        result.Errors.Any(e =>
            string.Equals(e.Code, "DuplicateEmail", StringComparison.Ordinal) ||
            string.Equals(e.Code, "DuplicateUserName", StringComparison.Ordinal));

    private static bool IsIncorrectPassword(IdentityResult result) =>
        result.Errors.Any(e => string.Equals(e.Code, "PasswordMismatch", StringComparison.Ordinal));

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    /// <summary>Encodes an Identity token so it survives being placed in a URL query string.</summary>
    private static string Encode(string token) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    private static bool TryDecode(string token, out string decoded)
    {
        try
        {
            decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
            return true;
        }
        catch (FormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }
}
