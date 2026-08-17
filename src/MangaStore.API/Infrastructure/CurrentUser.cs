namespace MangaStore.API.Infrastructure;

using System.Security.Claims;
using MangaStore.Application.Common.Identity;

/// <inheritdoc cref="ICurrentUser"/>
/// <remarks>Lives in the API layer because it is the only layer that knows about <c>HttpContext</c>.</remarks>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initialises a new instance of <see cref="CurrentUser"/>.</summary>
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public Guid? Id =>
        Guid.TryParse(Principal?.FindFirstValue(AppClaimTypes.Subject), out var id) ? id : null;

    /// <inheritdoc/>
    public string? Email => Principal?.FindFirstValue(AppClaimTypes.Email);

    /// <inheritdoc/>
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc/>
    public string? IpAddress =>
        _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;
}
