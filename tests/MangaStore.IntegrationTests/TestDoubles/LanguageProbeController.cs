namespace MangaStore.IntegrationTests.TestDoubles;

using MangaStore.API.Controllers.Base;
using MangaStore.Application.Common.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>Reports the language resolved for the request.</summary>
/// <remarks>
/// <c>IRequestLanguage</c> is implemented in the API layer, which the unit-test project does not
/// reference; adding that reference would pull the whole web SDK into the unit suite for one class.
/// </remarks>
[Route("api/v1/test-language")]
public sealed class LanguageProbeController : ApiControllerBase
{
    private readonly IRequestLanguage _requestLanguage;

    /// <summary>Initialises a new instance of <see cref="LanguageProbeController"/>.</summary>
    public LanguageProbeController(IRequestLanguage requestLanguage)
    {
        _requestLanguage = requestLanguage;
    }

    /// <summary>Returns the resolved language code.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<LanguageProbeResponse>(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new LanguageProbeResponse(_requestLanguage.Code));
}

/// <summary>Probe payload.</summary>
/// <param name="Code">The resolved language code.</param>
public sealed record LanguageProbeResponse(string Code);
