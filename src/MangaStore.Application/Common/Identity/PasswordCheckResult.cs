namespace MangaStore.Application.Common.Identity;

/// <summary>Outcome of verifying a password against a stored account.</summary>
public enum PasswordCheckResult
{
    /// <summary>The password did not match. Never distinguish this from an unknown account when reporting to the caller.</summary>
    InvalidCredentials = 0,

    /// <summary>The password matched.</summary>
    Success = 1,

    /// <summary>Too many failed attempts; the account is temporarily locked regardless of whether the password was correct.</summary>
    LockedOut = 2,
}
