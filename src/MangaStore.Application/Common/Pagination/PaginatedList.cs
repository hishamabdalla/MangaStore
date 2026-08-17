namespace MangaStore.Application.Common.Pagination;

/// <summary>A slice of a larger result set together with pagination metadata.</summary>
/// <typeparam name="TItem">The item type.</typeparam>
public sealed class PaginatedList<TItem>
{
    /// <summary>Initialises a new instance of <see cref="PaginatedList{TItem}"/>.</summary>
    /// <param name="items">The items on the current page.</param>
    /// <param name="totalCount">Total number of items across all pages.</param>
    /// <param name="pageNumber">1-based current page number.</param>
    /// <param name="pageSize">Maximum items per page.</param>
    public PaginatedList(IReadOnlyList<TItem> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    /// <summary>Gets the items on the current page.</summary>
    public IReadOnlyList<TItem> Items { get; }

    /// <summary>Gets the total number of items across all pages.</summary>
    public int TotalCount { get; }

    /// <summary>Gets the 1-based current page number.</summary>
    public int PageNumber { get; }

    /// <summary>Gets the maximum number of items per page.</summary>
    public int PageSize { get; }

    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Gets a value indicating whether a previous page exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>Gets a value indicating whether a next page exists.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>Creates a new <see cref="PaginatedList{TItem}"/>.</summary>
    public static PaginatedList<TItem> Create(
        IReadOnlyList<TItem> items,
        int totalCount,
        int pageNumber,
        int pageSize) =>
        new(items, totalCount, pageNumber, pageSize);
}
