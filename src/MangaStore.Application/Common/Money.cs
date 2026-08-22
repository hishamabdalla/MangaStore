namespace MangaStore.Application.Common;

/// <summary>Money arithmetic shared by every pricing path.</summary>
public static class Money
{
    /// <summary>Rounds <paramref name="value"/> to whole cents, half away from zero.</summary>
    /// <param name="value">The amount to round.</param>
    /// <remarks>
    /// The storefront rounds with <c>Math.round(value * 100) / 100</c>, which takes a half toward
    /// positive infinity. The two agree on every non-negative amount, and no figure this domain
    /// produces is negative, so the difference is unreachable in practice.
    /// </remarks>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
