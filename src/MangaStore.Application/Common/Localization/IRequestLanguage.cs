namespace MangaStore.Application.Common.Localization;

/// <summary>Resolves the language the current request asked to be served in.</summary>
public interface IRequestLanguage
{
    /// <summary>Gets the resolved language code, always one of <see cref="SupportedLanguages.All"/>.</summary>
    string Code { get; }
}
