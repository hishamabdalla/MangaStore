namespace MangaStore.API.Infrastructure;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MangaStore.Domain.Exceptions;

/// <summary>Handles unhandled exceptions and writes a <see cref="ProblemDetails"/> response. Replaces custom middleware.</summary>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>Initialises a new instance of <see cref="GlobalExceptionHandler"/>.</summary>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        string correlationId = httpContext.TraceIdentifier;

        (int status, string title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            Log.UnhandledException(_logger, correlationId, exception);
        }
        else
        {
            Log.KnownException(_logger, title, correlationId, exception);
        }

        bool isDevelopment = _environment.IsDevelopment();

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError && !isDevelopment
                ? "An unexpected error occurred."
                : exception.Message,
        };
        problem.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception. CorrelationId: {CorrelationId}")]
        public static partial void UnhandledException(ILogger logger, string correlationId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Title} exception. CorrelationId: {CorrelationId}")]
        public static partial void KnownException(ILogger logger, string title, string correlationId, Exception exception);
    }
}
