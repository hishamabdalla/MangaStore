namespace MangaStore.Application.Common.Validation;

using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MangaStore.Application.Common;

/// <inheritdoc cref="IValidationService"/>
public sealed partial class ValidationService : IValidationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ValidationService> _logger;

    /// <summary>Initialises a new instance of <see cref="ValidationService"/>.</summary>
    public ValidationService(IServiceProvider serviceProvider, ILogger<ValidationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result> ValidateAsync<TRequest>(TRequest request, CancellationToken ct = default)
    {
        var validator = _serviceProvider.GetService<IValidator<TRequest>>();

        if (validator is null)
        {
            Log.NoValidatorRegistered(_logger, typeof(TRequest).Name);
            return Result.Ok();
        }

        var validationResult = await validator.ValidateAsync(request, ct);
        if (validationResult.IsValid)
        {
            return Result.Ok();
        }

        string message = string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
        Log.ValidationFailed(_logger, typeof(TRequest).Name, message);
        return Result.Fail(ResultError.Validation(message));
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "No IValidator<{RequestType}> registered. Validation was skipped.")]
        public static partial void NoValidatorRegistered(ILogger logger, string requestType);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Validation failed for {RequestType}: {Errors}")]
        public static partial void ValidationFailed(ILogger logger, string requestType, string errors);
    }
}
