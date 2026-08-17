namespace MangaStore.API.Controllers.Base;

using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using MangaStore.Application.Common;

/// <summary>Base controller that wires <see cref="Result{TValue}"/> outcomes to HTTP responses.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion(1)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Maps a service result to the appropriate 2xx or 4xx response.</summary>
    /// <param name="result">The result returned by the service.</param>
    protected IActionResult HandleResult<TValue>(Result<TValue> result) =>
        result.IsSuccess ? Ok(result.Value) : MapErrorToResponse(result.Error);

    /// <summary>Returns 201 Created on success, or delegates to <see cref="HandleResult{TValue}"/> on failure.</summary>
    /// <param name="result">The result returned by the service.</param>
    /// <param name="actionName">Name of the GET action used to build the <c>Location</c> header.</param>
    /// <param name="routeValues">Route values forwarded to <c>CreatedAtAction</c>.</param>
    protected IActionResult HandleCreated<TValue>(Result<TValue> result, string actionName, object routeValues) =>
        result.IsSuccess ? CreatedAtAction(actionName, routeValues, result.Value) : MapErrorToResponse(result.Error);

    /// <summary>Returns 201 Created with an explicit <c>Location</c> URI, or delegates to <see cref="HandleResult{TValue}"/> on failure.</summary>
    /// <param name="result">The result returned by the service.</param>
    /// <param name="location">URI of the created resource, for responses whose body is not itself addressable.</param>
    protected IActionResult HandleCreated<TValue>(Result<TValue> result, string location) =>
        result.IsSuccess ? Created(location, result.Value) : MapErrorToResponse(result.Error);

    /// <summary>Returns 204 No Content on success, or a 4xx problem response on failure.</summary>
    /// <param name="result">The result returned by the service.</param>
    protected IActionResult HandleDelete(Result result) =>
        result.IsSuccess ? NoContent() : MapErrorToResponse(result.Error);

    private ObjectResult MapErrorToResponse(ResultError error) => error.Code switch
    {
        ResultErrorCodes.NotFound => NotFound(CreateProblem(404, error.Title, error.Message)),
        ResultErrorCodes.Conflict => Conflict(CreateProblem(409, error.Title, error.Message)),
        // StatusCode rather than Unauthorized() so the response carries a ProblemDetails body
        // instead of triggering a bodyless auth challenge — same reason Forbid() is avoided below.
        ResultErrorCodes.Unauthorized => StatusCode(StatusCodes.Status401Unauthorized, CreateProblem(401, error.Title, error.Message)),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, CreateProblem(403, error.Title, error.Message)),
        ResultErrorCodes.Validation => UnprocessableEntity(CreateProblem(422, error.Title, error.Message)),
        _ => BadRequest(CreateProblem(400, error.Title, error.Message)),
    };

    private static ProblemDetails CreateProblem(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}
