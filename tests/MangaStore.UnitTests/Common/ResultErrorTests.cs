namespace MangaStore.UnitTests.Common;

using MangaStore.Application.Common;
using Shouldly;
using Xunit;

public class ResultErrorTests
{
    /// <summary>The storefront switches on titles like <c>Coupon.Expired</c>, so the shape is a contract.</summary>
    [Fact]
    public void Validation_WithEntityAndReason_QualifiesTitle()
    {
        var error = ResultError.Validation("Coupon", "Expired", "That code has expired.");

        error.Title.ShouldBe("Coupon.Expired");
        error.Code.ShouldBe(ResultErrorCodes.Validation);
        error.Message.ShouldBe("That code has expired.");
    }

    [Fact]
    public void Validation_WithEntity_QualifiesTitleWithTheValidationCode()
    {
        var error = ResultError.Validation("Coupon", "That code cannot be used here.");

        error.Title.ShouldBe($"Coupon.{ResultErrorCodes.Validation}");
        error.Code.ShouldBe(ResultErrorCodes.Validation);
    }

    /*
     * The single-argument overload must keep its bare "Validation" title: ValidationService packs
     * field errors behind it, and the client only parses `detail` into per-field messages when the
     * response is a 422 carrying that title.
     */
    [Fact]
    public void Validation_WithMessageOnly_KeepsBareTitle()
    {
        var error = ResultError.Validation("Email: 'Email' is not a valid email address.");

        error.Title.ShouldBe(ResultErrorCodes.Validation);
        error.Code.ShouldBe(ResultErrorCodes.Validation);
    }

    [Fact]
    public void Validation_EveryOverload_MapsToTheSameCode()
    {
        ResultError.Validation("a").Code
            .ShouldBe(ResultError.Validation("Coupon", "b").Code);

        ResultError.Validation("Coupon", "b").Code
            .ShouldBe(ResultError.Validation("Coupon", "Expired", "c").Code);
    }
}
