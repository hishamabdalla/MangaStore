namespace MangaStore.Application.Features.Users.Profiles;

using AutoMapper;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Features.Users.Dtos;

/// <summary>Maps identity snapshots to the user read model.</summary>
public sealed class UserProfile : Profile
{
    /// <summary>Initialises a new instance of <see cref="UserProfile"/>.</summary>
    public UserProfile()
    {
        CreateMap<AppUserInfo, UserDto>();
    }
}
