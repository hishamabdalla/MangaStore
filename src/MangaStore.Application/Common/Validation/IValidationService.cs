namespace MangaStore.Application.Common.Validation;

using MangaStore.Application.Common;

/// <summary>Resolves and executes the FluentValidation validator registered for a request type.</summary>
public interface IValidationService
{
    /// <summary>
    /// Validates <paramref name="request"/> using its registered <c>IValidator&lt;TRequest&gt;</c>.
    /// Returns <see cref="Result.Ok()"/> when no validator is registered.
    /// </summary>
    /// <param name="request">The object to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A successful result, or a <c>Validation</c>-coded failure with all messages joined by <c>"; "</c>.</returns>
    Task<Result> ValidateAsync<TRequest>(TRequest request, CancellationToken ct = default);
}
