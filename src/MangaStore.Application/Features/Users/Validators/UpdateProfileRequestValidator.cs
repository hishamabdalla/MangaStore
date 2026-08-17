namespace MangaStore.Application.Features.Users.Validators;

using FluentValidation;
using MangaStore.Application.Features.Users.Dtos;

/// <summary>Validates <see cref="UpdateProfileRequest"/>.</summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    /// <summary>Initialises a new instance of <see cref="UpdateProfileRequestValidator"/>.</summary>
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);
    }
}
