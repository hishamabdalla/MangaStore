namespace MangaStore.Application.Common;

/// <summary>Well-known error code constants used by <see cref="ResultError"/> factory methods and mapped to HTTP status codes by the base controller.</summary>
public static class ResultErrorCodes
{
    /// <summary>Mapped to HTTP 404 Not Found.</summary>
    public const string NotFound = "NotFound";

    /// <summary>Mapped to HTTP 409 Conflict.</summary>
    public const string Conflict = "Conflict";

    /// <summary>Mapped to HTTP 401 Unauthorized.</summary>
    public const string Unauthorized = "Unauthorized";

    /// <summary>Mapped to HTTP 403 Forbidden.</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>Mapped to HTTP 422 Unprocessable Entity.</summary>
    public const string Validation = "Validation";

    /// <summary>Mapped to HTTP 400 Bad Request.</summary>
    public const string Failure = "Failure";
}
