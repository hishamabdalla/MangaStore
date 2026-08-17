namespace MangaStore.Application.Features.Users;

using AutoMapper;
using MangaStore.Application.Common;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Common.Validation;
using MangaStore.Application.Features.Users.Dtos;
using Microsoft.Extensions.Logging;

/// <inheritdoc cref="IUserService"/>
public sealed partial class UserService : IUserService
{
    private const string ErrorEntity = "User";

    private readonly IIdentityService _identityService;
    private readonly ICurrentUser _currentUser;
    private readonly IValidationService _validationService;
    private readonly IMapper _mapper;
    private readonly ILogger<UserService> _logger;

    /// <summary>Initialises a new instance of <see cref="UserService"/>.</summary>
    public UserService(
        IIdentityService identityService,
        ICurrentUser currentUser,
        IValidationService validationService,
        IMapper mapper,
        ILogger<UserService> logger)
    {
        _identityService = identityService;
        _currentUser = currentUser;
        _validationService = validationService;
        _mapper = mapper;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<UserDto>> GetCurrentAsync(CancellationToken ct = default)
    {
        if (_currentUser.Id is not Guid userId)
            return ResultError.Unauthorized(ErrorEntity, "Not authenticated.");

        var user = await _identityService.FindByIdAsync(userId, ct);
        if (user is null)
        {
            // A valid signature over an account that no longer exists — deleted mid-session.
            Log.TokenUserMissing(_logger, userId);
            return ResultError.NotFound(ErrorEntity, "This account no longer exists.");
        }

        return _mapper.Map<UserDto>(user);
    }

    /// <inheritdoc/>
    public async Task<Result<UserDto>> UpdateCurrentAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validationService.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return validation.Error;

        if (_currentUser.Id is not Guid userId)
            return ResultError.Unauthorized(ErrorEntity, "Not authenticated.");

        var updated = await _identityService.UpdateDisplayNameAsync(userId, request.DisplayName, ct);
        if (updated.IsFailure)
            return updated.Error;

        var user = await _identityService.FindByIdAsync(userId, ct);
        if (user is null)
        {
            Log.TokenUserMissing(_logger, userId);
            return ResultError.NotFound(ErrorEntity, "This account no longer exists.");
        }

        Log.ProfileUpdated(_logger, userId);
        return _mapper.Map<UserDto>(user);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Token presented for user {UserId}, which no longer exists.")]
        public static partial void TokenUserMissing(ILogger logger, Guid userId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Profile updated for user {UserId}.")]
        public static partial void ProfileUpdated(ILogger logger, Guid userId);
    }
}
