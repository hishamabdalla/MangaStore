namespace MangaStore.Application.Features.Users.Dtos;

/// <summary>Read model describing a user account.</summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="Email">Email address, which doubles as the login name.</param>
/// <param name="DisplayName">Name shown to other users.</param>
/// <param name="Roles">Role names assigned to the account.</param>
/// <param name="CreatedAt">UTC instant the account was created.</param>
public sealed record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, DateTime CreatedAt);
