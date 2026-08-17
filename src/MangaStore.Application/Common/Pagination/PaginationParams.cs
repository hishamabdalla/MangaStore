namespace MangaStore.Application.Common.Pagination;

/// <summary>Query-string parameters that control pagination. Both values are clamped to safe ranges.</summary>
public sealed class PaginationParams
{
    private const int MaxPageSize = 100;
    private int _pageNumber = 1;
    private int _pageSize = 20;

    /// <summary>Gets the 1-based page number. Values below 1 are raised to 1. Defaults to <c>1</c>.</summary>
    public int PageNumber
    {
        get => _pageNumber;
        init => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>Gets the number of items per page. Values below 1 are raised to 1; values above 100 are capped at 100. Defaults to <c>20</c>.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value > MaxPageSize ? MaxPageSize : value < 1 ? 1 : value;
    }

    /// <summary>Gets the number of items to skip, derived from <see cref="PageNumber"/> and <see cref="PageSize"/>.</summary>
    public int Skip => (PageNumber - 1) * PageSize;
}
