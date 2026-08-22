namespace MangaStore.IntegrationTests.TestDoubles;

using MangaStore.API.Controllers.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>An enum and a timestamp on the wire, so the serialisation conventions can be asserted directly.</summary>
/// <remarks>
/// Exists because Phase 01 ships no endpoint of its own. Once the catalogue lands, its real
/// responses cover this and the probe can be deleted.
/// </remarks>
[Route("api/v1/test-serialization")]
public sealed class SerializationProbeController : ApiControllerBase
{
    /// <summary>Returns one of each value the wire conventions apply to.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<SerializationProbeResponse>(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new SerializationProbeResponse(
        ProbeStockStatus.PreOrder,
        new DateTime(2026, 5, 14, 22, 4, 21, DateTimeKind.Unspecified),
        null,
        new DateOnly(2026, 5, 14)));
}

/// <summary>Stands in for the catalogue's <c>StockStatus</c>, which does not exist until Phase 02.</summary>
public enum ProbeStockStatus
{
    /// <summary>Available now.</summary>
    InStock,

    /// <summary>At or below the low-stock threshold.</summary>
    LowStock,

    /// <summary>Not yet released.</summary>
    PreOrder,

    /// <summary>None left.</summary>
    OutOfStock,
}

/// <summary>Probe payload.</summary>
/// <param name="Status">Must serialise as the camelCase string <c>preOrder</c>.</param>
/// <param name="Timestamp">Unspecified kind, as EF hands one back; must serialise with a <c>Z</c>.</param>
/// <param name="OptionalTimestamp">Must serialise as <see langword="null"/>, not as an empty string.</param>
/// <param name="ReleasedOn">Must serialise as a bare <c>YYYY-MM-DD</c> with no time and no designator.</param>
public sealed record SerializationProbeResponse(
    ProbeStockStatus Status,
    DateTime Timestamp,
    DateTime? OptionalTimestamp,
    DateOnly ReleasedOn);
