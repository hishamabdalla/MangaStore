namespace MangaStore.UnitTests.Common;

using System.Text.RegularExpressions;
using FluentValidation;
using MangaStore.Application.Common;
using MangaStore.Application.Common.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

/*
 * Pins the packed 422 string, because the storefront reverse-engineers it.
 * ErrorMessageService.parseFieldErrors splits `detail` on "; ", then each entry on its first ": ",
 * rejects any property name failing /^[A-Za-z][A-Za-z0-9.]*$/, and lower-cases the first character
 * of each dot-segment to find the reactive-form control. Change the format and every inline field
 * error silently disappears from the UI — no error, no warning, just blank inputs.
 */
public class ValidationServiceTests
{
    private static readonly Regex FrontendPropertyPattern = new("^[A-Za-z][A-Za-z0-9.]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task ValidateAsync_WithFailures_JoinsEntriesWithSemicolonSpace()
    {
        var result = await ValidateAsync(new SampleRequest("not-an-email", "abc"));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Title.ShouldBe(ResultErrorCodes.Validation);
        result.Error.Message.ShouldContain("; ");
        result.Error.Message.ShouldStartWith("Email: ");
    }

    [Fact]
    public async Task ValidateAsync_EveryEntry_SplitsOnTheFirstColonSpace()
    {
        var result = await ValidateAsync(new SampleRequest("not-an-email", "abc"));

        foreach (string entry in result.Error.Message.Split("; "))
        {
            entry.IndexOf(": ", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public async Task ValidateAsync_PropertyNames_SurviveTheClientsPropertyPattern()
    {
        var result = await ValidateAsync(new SampleRequest("not-an-email", "abc"));

        var parsed = ParseLikeTheStorefront(result.Error.Message);

        parsed.ShouldNotBeEmpty();
        parsed.Keys.ShouldContain("email");
        parsed.Keys.ShouldContain("password");
    }

    /// <summary>Two failures on one property arrive as two entries and are joined by the client, not the server.</summary>
    [Fact]
    public async Task ValidateAsync_RepeatedProperty_EmitsOneEntryPerFailure()
    {
        var result = await ValidateAsync(new SampleRequest("a@b.co", "abc"));

        result.Error.Message
            .Split("; ")
            .Count(entry => entry.StartsWith("Password: ", StringComparison.Ordinal))
            .ShouldBe(2);
    }

    [Fact]
    public async Task ValidateAsync_WhenValid_Succeeds()
    {
        var result = await ValidateAsync(new SampleRequest("a@b.co", "Abcdefgh"));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithNoRegisteredValidator_Succeeds()
    {
        var provider = Substitute.For<IServiceProvider>();
        // An unconfigured substitute already returns null for the validator lookup.
        var service = new ValidationService(provider, NullLogger<ValidationService>.Instance);

        var result = await service.ValidateAsync(new SampleRequest("not-an-email", "abc"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    private static async Task<Result> ValidateAsync(SampleRequest request)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IValidator<SampleRequest>)).Returns(new SampleRequestValidator());
        var service = new ValidationService(provider, NullLogger<ValidationService>.Instance);

        return await service.ValidateAsync(request, CancellationToken.None);
    }

    /// <summary>A faithful port of the storefront's <c>parseFieldErrors</c>.</summary>
    private static Dictionary<string, string> ParseLikeTheStorefront(string detail)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string entry in detail.Split("; "))
        {
            int separator = entry.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string property = entry[..separator].Trim();
            string message = entry[(separator + 2)..].Trim();

            if (!FrontendPropertyPattern.IsMatch(property))
            {
                continue;
            }

            string field = string.Join('.', property.Split('.').Select(part => char.ToLowerInvariant(part[0]) + part[1..]));
            errors[field] = errors.TryGetValue(field, out string? existing) ? $"{existing} {message}" : message;
        }

        return errors;
    }

    public sealed record SampleRequest(string Email, string Password);

    private sealed class SampleRequestValidator : AbstractValidator<SampleRequest>
    {
        public SampleRequestValidator()
        {
            RuleFor(x => x.Email).EmailAddress();
            RuleFor(x => x.Password).MinimumLength(8);
            RuleFor(x => x.Password)
                .Must(password => password.Any(char.IsUpper))
                .WithMessage("'Password' must contain an uppercase letter.");
        }
    }
}
