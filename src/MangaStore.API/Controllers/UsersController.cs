namespace MangaStore.API.Controllers;

using MangaStore.API.Controllers.Base;
using MangaStore.Application.Features.Users;
using MangaStore.Application.Features.Users.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Manages user profiles.</summary>
[Authorize]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserService _userService;

    /// <summary>Initialises a new instance of <see cref="UsersController"/>.</summary>
    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>Returns the signed-in account.</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentAsync(CancellationToken ct) =>
        HandleResult(await _userService.GetCurrentAsync(ct));

    /// <summary>Updates the signed-in account's profile.</summary>
    /// <param name="request">Replacement profile values.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCurrentAsync([FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        HandleResult(await _userService.UpdateCurrentAsync(request, ct));
}
