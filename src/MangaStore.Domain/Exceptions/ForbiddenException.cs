namespace MangaStore.Domain.Exceptions;

/// <summary>Thrown by infrastructure when the caller lacks permission for the requested resource. Mapped to HTTP 403 by the exception handler.</summary>
public sealed class ForbiddenException : Exception
{
    /// <inheritdoc/>
    public ForbiddenException(string message) : base(message) { }

    /// <inheritdoc/>
    public ForbiddenException(string message, Exception innerException) : base(message, innerException) { }
}
