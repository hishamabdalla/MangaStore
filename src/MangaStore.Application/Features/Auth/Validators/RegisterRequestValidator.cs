namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Validates <see cref="RegisterRequest"/>.</summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    /// <summary>Initialises a new instance of <see cref="RegisterRequestValidator"/>.</summary>
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).Email();
        RuleFor(x => x.Password).Password();

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);
    }
}
