namespace MangaStore.UnitTests.Common;

using MangaStore.Application.Common.Pagination;
using Shouldly;
using Xunit;

public sealed class PaginationParamsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void PageNumber_ClampsToMinimumOfOne(int input, int expected)
    {
        var p = new PaginationParams { PageNumber = input };
        p.PageNumber.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(999, 100)]
    public void PageSize_ClampsToRange(int input, int expected)
    {
        var p = new PaginationParams { PageSize = input };
        p.PageSize.ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 20, 40)]
    [InlineData(5, 100, 400)]
    public void Skip_IsPageMinusOneTimesPageSize(int pageNumber, int pageSize, int expectedSkip)
    {
        var p = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
        p.Skip.ShouldBe(expectedSkip);
    }
}
