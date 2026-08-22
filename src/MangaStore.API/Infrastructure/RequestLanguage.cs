namespace MangaStore.API.Infrastructure;

using MangaStore.Application.Common.Localization;
using Microsoft.Net.Http.Headers;

/// <inheritdoc cref="IRequestLanguage"/>
/// <remarks>
/// Lives in the API layer because it is the only layer that knows about <c>HttpContext</c>.
/// Reads <c>Accept-Language</c> directly rather than through <c>AddRequestLocalization</c>: the
/// framework middleware also sets the thread culture, which would change number and date formatting
/// across the whole request for a choice that only concerns which translation row to select.
/// </remarks>
public sealed class RequestLanguage : IRequestLanguage
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initialises a new instance of <see cref="RequestLanguage"/>.</summary>
    public RequestLanguage(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string Code
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null)
            {
                return SupportedLanguages.Default;
            }

            // TryParseList rather than ParseList: a malformed header is a bad request from a
            // stranger, not an exception, and must not take the response down with it.
            if (!StringWithQualityHeaderValue.TryParseList(context.Request.Headers.AcceptLanguage, out var requested)
                || requested is null)
            {
                return SupportedLanguages.Default;
            }

            foreach (var candidate in requested.Where(v => v.Quality != 0).OrderByDescending(v => v.Quality ?? 1.0))
            {
                string primary = PrimarySubtag(candidate.Value.Value);
                if (SupportedLanguages.IsSupported(primary))
                {
                    return primary.ToLowerInvariant();
                }
            }

            return SupportedLanguages.Default;
        }
    }

    /// <summary>Reduces a language range such as <c>ar-EG</c> to its primary subtag.</summary>
    private static string PrimarySubtag(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        int separator = value.IndexOf('-', StringComparison.Ordinal);
        return separator < 0 ? value : value[..separator];
    }
}
