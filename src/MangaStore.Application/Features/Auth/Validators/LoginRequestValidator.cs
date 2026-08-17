namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;
using MangaStore.Application.Features.Auth.Dtos;

/// <summary>Validates <see cref="LoginRequest"/>.</summary>
/// <remarks>
/// Deliberately checks presence only. Applying the password complexity rules here would reject a
/// wrong password with 422 before the credentials are ever checked, telling an attacker the guess
/// was malformed rather than simply wrong.
/// </remarks>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    /// <summary>Initialises a new instance of <see cref="LoginRequestValidator"/>.</summary>
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
