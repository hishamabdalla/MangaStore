namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Validates <see cref="RefreshTokenRequest"/>.</summary>
public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    /// <summary>Initialises a new instance of <see cref="RefreshTokenRequestValidator"/>.</summary>
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
    }
}
