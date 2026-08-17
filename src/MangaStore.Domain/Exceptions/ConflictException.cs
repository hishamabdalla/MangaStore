namespace MangaStore.Domain.Exceptions;

/// <summary>Thrown by infrastructure when an operation violates a uniqueness constraint. Mapped to HTTP 409 by the exception handler.</summary>
public sealed class ConflictException : Exception
{
    /// <inheritdoc/>
    public ConflictException(string message) : base(message) { }

    /// <inheritdoc/>
    public ConflictException(string message, Exception innerException) : base(message, innerException) { }
}
