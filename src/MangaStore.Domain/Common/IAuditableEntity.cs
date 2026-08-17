namespace MangaStore.Domain.Common;

/// <summary>Marks an entity whose creation and update timestamps are maintained automatically by the audit interceptor.</summary>
public interface IAuditableEntity
{
    /// <summary>Gets or sets the UTC timestamp when the entity was created.</summary>
    DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last update, or <see langword="null"/> if never updated.</summary>
    DateTime? UpdatedAt { get; set; }
}
