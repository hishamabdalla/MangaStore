namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Validates <see cref="ForgotPasswordRequest"/>.</summary>
public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    /// <summary>Initialises a new instance of <see cref="ForgotPasswordRequestValidator"/>.</summary>
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).Email();
    }
}
