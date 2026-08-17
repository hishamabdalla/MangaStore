namespace MangaStore.Application.Common.Email;

/// <summary>Delivers transactional email.</summary>
/// <remarks>
/// One method for now. The interface exists so swapping the development logging implementation
/// for a real SMTP or provider client is a single class with no changes to callers.
/// </remarks>
public interface IEmailSender
{
    /// <summary>Sends a password reset link.</summary>
    /// <param name="email">Recipient address.</param>
    /// <param name="resetLink">Absolute URL containing the reset token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendPasswordResetAsync(string email, string resetLink, CancellationToken ct = default);
}
