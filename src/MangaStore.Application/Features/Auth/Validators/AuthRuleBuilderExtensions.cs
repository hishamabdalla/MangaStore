namespace MangaStore.Application.Features.Auth.Validators;

using FluentValidation;

/// <summary>Shared rules for the credential fields that recur across the auth requests.</summary>
/// <remarks>
/// The password rules mirror the <c>IdentityOptions</c> policy configured in Infrastructure. Both
/// must be changed together; validating here only buys a clearer 422 before Identity would reject it.
/// </remarks>
public static class AuthRuleBuilderExtensions
{
    /// <summary>Minimum password length, mirroring <c>IdentityOptions.Password.RequiredLength</c>.</summary>
    public const int MinimumPasswordLength = 8;

    /// <summary>Applies the email format and length rules.</summary>
    /// <typeparam name="T">The request type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder for the email property.</param>
    public static IRuleBuilderOptions<T, string> Email<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);

        return ruleBuilder
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();
    }

    /// <summary>Applies the configured password complexity rules.</summary>
    /// <typeparam name="T">The request type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder for the password property.</param>
    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);

        return ruleBuilder
            .NotEmpty()
            .MinimumLength(MinimumPasswordLength)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("'{PropertyName}' must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("'{PropertyName}' must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("'{PropertyName}' must contain a digit.");
    }
}
