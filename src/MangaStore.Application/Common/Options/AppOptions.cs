namespace MangaStore.Application.Common.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>General application settings bound from the <c>App</c> section of <c>appsettings.json</c>.</summary>
public sealed class AppOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "App";

    /// <summary>Gets the base URL of the storefront front end, used to build links sent by email.</summary>
    [Required, Url]
    public string FrontendBaseUrl { get; init; } = string.Empty;
}
