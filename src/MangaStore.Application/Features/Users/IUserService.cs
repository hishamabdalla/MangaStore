namespace MangaStore.Application.Features.Users;

using MangaStore.Application.Common;
using MangaStore.Application.Features.Users.Dtos;

/// <summary>Reads and updates the signed-in account's own profile.</summary>
public interface IUserService
{
    /// <summary>Returns the signed-in account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account, or a not-found error if the token names a user that no longer exists.</returns>
    Task<Result<UserDto>> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Updates the signed-in account's profile and returns it.</summary>
    /// <param name="request">Replacement profile values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<UserDto>> UpdateCurrentAsync(UpdateProfileRequest request, CancellationToken ct = default);
}
