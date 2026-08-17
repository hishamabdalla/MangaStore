namespace MangaStore.Domain.Exceptions;

/// <summary>Thrown by infrastructure when a requested entity does not exist. Mapped to HTTP 404 by the exception handler.</summary>
public sealed class NotFoundException : Exception
{
    /// <inheritdoc/>
    public NotFoundException(string message) : base(message) { }

    /// <inheritdoc/>
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Initialises the exception with a standardised message derived from the entity type and identifier.</summary>
    /// <param name="entityName">Name of the entity type (e.g. <c>"Product"</c>).</param>
    /// <param name="id">The identifier that was not found.</param>
    public NotFoundException(string entityName, Guid id)
        : base($"Entity \"{entityName}\" ({id}) was not found.")
    {
        EntityName = entityName;
        EntityId = id;
    }

    /// <summary>Gets the entity type name, if constructed with <see cref="NotFoundException(string, Guid)"/>.</summary>
    public string? EntityName { get; }

    /// <summary>Gets the identifier that was not found, if constructed with <see cref="NotFoundException(string, Guid)"/>.</summary>
    public Guid EntityId { get; }
}
