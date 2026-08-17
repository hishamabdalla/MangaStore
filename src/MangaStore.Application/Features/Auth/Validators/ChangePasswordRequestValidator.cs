namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Validates <see cref="ChangePasswordRequest"/>.</summary>
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    /// <summary>Initialises a new instance of <see cref="ChangePasswordRequestValidator"/>.</summary>
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).Password();

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must differ from the current one.");
    }
}
