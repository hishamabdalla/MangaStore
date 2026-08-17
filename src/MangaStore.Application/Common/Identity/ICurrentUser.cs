namespace MangaStore.Application.Common.Identity;

/// <summary>Exposes the caller behind the current request.</summary>
/// <remarks>Implemented in the API layer, which is the only layer that knows about <c>HttpContext</c>.</remarks>
public interface ICurrentUser
{
    /// <summary>Gets the remote IP address of the request, or <see langword="null"/> if unavailable. Recorded against issued refresh tokens for audit.</summary>
    string? IpAddress { get; }

    /// <summary>Gets the caller's identifier, or <see langword="null"/> when the request is anonymous.</summary>
    Guid? Id { get; }

    /// <summary>Gets the caller's email address, or <see langword="null"/> when the request is anonymous.</summary>
    string? Email { get; }

    /// <summary>Gets a value indicating whether the request carries a valid authenticated identity.</summary>
    bool IsAuthenticated { get; }
}
