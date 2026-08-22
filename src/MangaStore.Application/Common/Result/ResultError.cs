namespace MangaStore.Application.Common;

/// <summary>Describes why an operation failed. The <see cref="Code"/> drives HTTP status mapping in the base controller.</summary>
/// <param name="Code">Machine-readable error category (e.g. <c>"NotFound"</c>, <c>"Validation"</c>). Used for HTTP status mapping.</param>
/// <param name="Title">Entity-qualified title surfaced in <c>ProblemDetails.Title</c> (e.g. <c>"Product.NotFound"</c>).</param>
/// <param name="Message">Human-readable description surfaced in <c>ProblemDetails.Detail</c>.</param>
public sealed record ResultError(string Code, string Title, string Message)
{
    /// <summary>Sentinel used on successful results; never set on a failure.</summary>
    public static readonly ResultError None = new(string.Empty, string.Empty, string.Empty);

    /// <summary>Creates a 404-mapped error. Title and message are derived from the entity type and its identifier.</summary>
    /// <typeparam name="TEntity">The entity type — its name is used in the title and message.</typeparam>
    /// <param name="id">Identifier of the entity that was not found.</param>
    public static ResultError NotFound<TEntity>(Guid id)
    {
        string name = typeof(TEntity).Name;
        return new(ResultErrorCodes.NotFound, $"{name}.{ResultErrorCodes.NotFound}", $"{name} {id} was not found.");
    }

    /// <summary>Creates a 404-mapped error with a custom message.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title.</param>
    /// <param name="message">Human-readable description of the error.</param>
    public static ResultError NotFound(string entity, string message) =>
        new(ResultErrorCodes.NotFound, $"{entity}.{ResultErrorCodes.NotFound}", message);

    /// <summary>Creates a 409-mapped error.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title.</param>
    /// <param name="message">Human-readable description of the error.</param>
    public static ResultError Conflict(string entity, string message) =>
        new(ResultErrorCodes.Conflict, $"{entity}.{ResultErrorCodes.Conflict}", message);

    /// <summary>Creates a 401-mapped error, used when credentials are missing, wrong, or no longer valid.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title.</param>
    /// <param name="message">Human-readable description of the error. Must not reveal whether an account exists.</param>
    public static ResultError Unauthorized(string entity, string message) =>
        new(ResultErrorCodes.Unauthorized, $"{entity}.{ResultErrorCodes.Unauthorized}", message);

    /// <summary>Creates a 403-mapped error.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title.</param>
    /// <param name="message">Human-readable description of the error.</param>
    public static ResultError Forbidden(string entity, string message) =>
        new(ResultErrorCodes.Forbidden, $"{entity}.{ResultErrorCodes.Forbidden}", message);

    /// <summary>Creates a 422-mapped error for FluentValidation failures. No entity qualifier — validation errors are framework-aggregated.</summary>
    /// <param name="message">Human-readable description of the validation failure.</param>
    public static ResultError Validation(string message) =>
        new(ResultErrorCodes.Validation, ResultErrorCodes.Validation, message);

    /// <summary>Creates a 422-mapped error qualified by the entity it concerns.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title (e.g. <c>"Coupon"</c>).</param>
    /// <param name="message">Human-readable description of the validation failure.</param>
    public static ResultError Validation(string entity, string message) =>
        new(ResultErrorCodes.Validation, $"{entity}.{ResultErrorCodes.Validation}", message);

    /// <summary>Creates a 422-mapped error whose title names the specific rule that rejected the request.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title (e.g. <c>"Coupon"</c>).</param>
    /// <param name="reason">The rule that failed, forming the second half of the title (e.g. <c>"Expired"</c>).</param>
    /// <param name="message">Human-readable description of the validation failure.</param>
    /// <remarks>The storefront switches on the resulting title, so <c>Coupon.Expired</c> is a contract, not a label.</remarks>
    public static ResultError Validation(string entity, string reason, string message) =>
        new(ResultErrorCodes.Validation, $"{entity}.{reason}", message);

    /// <summary>Creates a generic 400-mapped error.</summary>
    /// <param name="entity">Entity or resource name used to qualify the title.</param>
    /// <param name="message">Human-readable description of the error.</param>
    public static ResultError Failure(string entity, string message) =>
        new(ResultErrorCodes.Failure, $"{entity}.{ResultErrorCodes.Failure}", message);
}
