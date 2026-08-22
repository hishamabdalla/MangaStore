namespace MangaStore.UnitTests.Common;

using MangaStore.Application.Common;
using Shouldly;
using Xunit;

public class MoneyTests
{
    /*
     * 2.005m is the case that discriminates. 1.275m does not: it is exactly representable as a
     * decimal, so banker's rounding picks the even last digit — 1.28 — which is also the
     * away-from-zero answer. A test written on 1.275m alone passes against a bare Math.Round.
     */
    [Theory]
    [InlineData(2.005, 2.01)]
    [InlineData(1.005, 1.01)]
    [InlineData(0.045, 0.05)]
    [InlineData(1.275, 1.28)]
    public void Round_HalfCase_RoundsAwayFromZero(decimal value, decimal expected) =>
        Money.Round(value).ShouldBe(expected);

    [Theory]
    [InlineData(-2.005, -2.01)]
    [InlineData(-1.005, -1.01)]
    public void Round_NegativeHalfCase_RoundsAwayFromZero(decimal value, decimal expected) =>
        Money.Round(value).ShouldBe(expected);

    [Fact]
    public void Round_DoesNotUseBankersRounding() =>
        Money.Round(2.005m).ShouldNotBe(Math.Round(2.005m, 2));

    /// <summary>10% of $12.75 is $1.275, and the storefront prints $1.28. The two must agree.</summary>
    [Fact]
    public void Round_MatchesTheStorefrontOnTheDocumentedHalfCentCase() =>
        Money.Round(12.75m * (10m / 100m)).ShouldBe(1.28m);

    [Theory]
    [InlineData(19.99, 19.99)]
    [InlineData(0, 0)]
    [InlineData(59.969999, 59.97)]
    public void Round_NonMidpoint_RoundsToNearest(decimal value, decimal expected) =>
        Money.Round(value).ShouldBe(expected);
}
