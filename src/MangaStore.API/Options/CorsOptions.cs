namespace MangaStore.API.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>CORS policy configuration bound from the <c>Cors</c> section of <c>appsettings.json</c>.</summary>
public sealed class CorsOptions
{
    /// <summary>Gets the list of allowed origins for the default CORS policy.</summary>
    [Required, MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];
}
