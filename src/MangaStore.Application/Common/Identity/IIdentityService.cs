namespace MangaStore.Application.Common.Identity;

using MangaStore.Application.Common;

/// <summary>The Application layer's seam over ASP.NET Core Identity.</summary>
/// <remarks>
/// Every member returns primitives, <see cref="AppUserInfo"/>, or <see cref="Result"/> — never an
/// Identity type. This is what keeps <c>UserManager</c> and <c>SignInManager</c> confined to Infrastructure.
/// </remarks>
public interface IIdentityService
{
    /// <summary>Creates an account in the <c>Customer</c> role and returns its identifier.</summary>
    /// <param name="email">Email address, which doubles as the login name.</param>
    /// <param name="password">Plain-text password; hashed by the implementation.</param>
    /// <param name="displayName">Name shown to other users.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new account, or a conflict error if the email is already registered.</returns>
    Task<Result<AppUserInfo>> CreateUserAsync(string email, string password, string displayName, CancellationToken ct = default);

    /// <summary>Returns the account with the given <paramref name="email"/>, or <see langword="null"/> if none exists.</summary>
    Task<AppUserInfo?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Returns the account with the given <paramref name="userId"/>, or <see langword="null"/> if none exists.</summary>
    Task<AppUserInfo?> FindByIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Verifies a password, incrementing the lockout counter on failure.</summary>
    /// <param name="userId">Account to check against.</param>
    /// <param name="password">Plain-text password to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken ct = default);

    /// <summary>Generates a single-use, time-limited password reset token, already encoded for safe transport in a URL.</summary>
    Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Sets a new password using a token from <see cref="GeneratePasswordResetTokenAsync"/>.</summary>
    /// <param name="userId">Account to reset.</param>
    /// <param name="token">Encoded token previously issued for this account.</param>
    /// <param name="newPassword">Replacement password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a validation error describing why the token or password was rejected.</returns>
    Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken ct = default);

    /// <summary>Changes a password, requiring the current one as proof of possession.</summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);

    /// <summary>Updates the name shown to other users.</summary>
    Task<Result> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default);
}
