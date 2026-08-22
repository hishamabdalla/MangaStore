namespace MangaStore.Application.Common.Localization;

/// <summary>The languages catalogue content is translated into.</summary>
/// <remarks>Mirrors the storefront's <c>environment.supportedLanguages</c>; <c>ar</c> is rendered right-to-left.</remarks>
public static class SupportedLanguages
{
    /// <summary>Language served when the request asks for nothing supported.</summary>
    public const string Default = "en";

    /// <summary>Gets every supported language code.</summary>
    public static IReadOnlyList<string> All { get; } = ["en", "ar"];

    /// <summary>Returns whether <paramref name="code"/> is supported, ignoring case.</summary>
    /// <param name="code">A language code, or <see langword="null"/>.</param>
    public static bool IsSupported(string? code) =>
        code is not null && All.Any(supported => string.Equals(supported, code, StringComparison.OrdinalIgnoreCase));
}
