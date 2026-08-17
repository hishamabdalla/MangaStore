namespace MangaStore.Infrastructure.Email;

using MangaStore.Application.Common.Email;
using Microsoft.Extensions.Logging;

/// <inheritdoc cref="IEmailSender"/>
/// <remarks>
/// Development implementation: writes the link to the log instead of sending it, so the reset flow
/// can be exercised end to end without an SMTP account. Replace the registration with a real sender
/// before any environment where the address belongs to someone other than the developer.
/// </remarks>
public sealed partial class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    /// <summary>Initialises a new instance of <see cref="LoggingEmailSender"/>.</summary>
    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken ct = default)
    {
        Log.PasswordReset(_logger, email, resetLink);
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "EMAIL (not sent) to {Email} | Password reset link: {ResetLink}")]
        public static partial void PasswordReset(ILogger logger, string email, string resetLink);
    }
}
