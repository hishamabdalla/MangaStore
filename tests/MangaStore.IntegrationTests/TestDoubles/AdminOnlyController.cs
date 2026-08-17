namespace MangaStore.IntegrationTests.TestDoubles;

using MangaStore.API.Controllers.Base;
using MangaStore.Domain.Features.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>A role-guarded endpoint that exists only to prove role claims survive the round trip.</summary>
/// <remarks>
/// Registered as an extra application part by the test factory. Once the catalogue ships its own
/// <c>[Authorize(Roles = Admin)]</c> endpoints this can be deleted and the tests pointed at a real one.
/// </remarks>
[Route("api/v1/test-admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminOnlyController : ApiControllerBase
{
    /// <summary>Returns 200 only for callers holding the Admin role.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new { ok = true });
}
