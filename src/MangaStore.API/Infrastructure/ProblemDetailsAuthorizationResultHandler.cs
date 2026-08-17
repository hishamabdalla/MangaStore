namespace MangaStore.API.Infrastructure;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

/// <summary>Gives authorization failures raised by the middleware the same <see cref="ProblemDetails"/> body that service-level failures get.</summary>
/// <remarks>
/// Without this, a request rejected by <c>[Authorize(Roles = ...)]</c> returns a bare 403 with no
/// body, so a client cannot tell an authorization failure from any other empty response. Challenges
/// (401) are still delegated to the default handler, which is what adds the <c>WWW-Authenticate</c> header.
/// </remarks>
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler Default = new();

    private readonly IProblemDetailsService _problemDetailsService;

    /// <summary>Initialises a new instance of <see cref="ProblemDetailsAuthorizationResultHandler"/>.</summary>
    public ProblemDetailsAuthorizationResultHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    /// <inheritdoc/>
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            await _problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Auth.Forbidden",
                    Detail = "You do not have permission to access this resource.",
                },
            });

            return;
        }

        await Default.HandleAsync(next, context, policy, authorizeResult);
    }
}
