namespace MangaStore.Domain.Interfaces;

/// <summary>Abstracts the system clock so that time-dependent code can be tested deterministically.</summary>
public interface IDateTime
{
    /// <summary>Gets the current UTC date and time.</summary>
    DateTime UtcNow { get; }
}
