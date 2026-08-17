namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Validates <see cref="ResetPasswordRequest"/>.</summary>
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    /// <summary>Initialises a new instance of <see cref="ResetPasswordRequestValidator"/>.</summary>
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).Email();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).Password();
    }
}
