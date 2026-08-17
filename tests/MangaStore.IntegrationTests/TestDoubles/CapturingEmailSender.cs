namespace MangaStore.IntegrationTests.TestDoubles;

using System.Collections.Concurrent;
using MangaStore.Application.Common.Email;

/// <summary>Captures outgoing email in memory so tests can read the links the application generated.</summary>
/// <remarks>Registered as a singleton so a test can inspect what the request pipeline produced.</remarks>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(string Email, string Link)> _passwordResets = new();

    /// <summary>Gets every password reset that has been "sent", oldest first.</summary>
    public IReadOnlyCollection<(string Email, string Link)> PasswordResets => _passwordResets;

    /// <inheritdoc/>
    public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken ct = default)
    {
        _passwordResets.Enqueue((email, resetLink));
        return Task.CompletedTask;
    }

    /// <summary>Returns the most recent reset link sent to <paramref name="email"/>.</summary>
    /// <param name="email">Recipient to look for.</param>
    /// <exception cref="InvalidOperationException">Thrown when no reset was sent to that address.</exception>
    public string LatestResetLinkFor(string email) =>
        _passwordResets.LastOrDefault(r => string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase)).Link
        ?? throw new InvalidOperationException($"No password reset email was sent to {email}.");
}
