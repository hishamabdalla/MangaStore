namespace MangaStore.UnitTests.Common;

using MangaStore.Application.Common;
using Shouldly;
using Xunit;

public sealed class ResultTests
{
    [Fact]
    public void Ok_NonGeneric_IsSuccessAndHasNoError()
    {
        var result = Result.Ok();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(ResultError.None);
    }

    [Fact]
    public void Fail_NonGeneric_IsFailureAndHasError()
    {
        var error = ResultError.NotFound("Test", "Not found.");
        var result = Result.Fail(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void Ok_Generic_IsSuccessAndReturnsValue()
    {
        var result = Result.Ok(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Fail_Generic_IsFailureAndThrowsOnValueAccess()
    {
        var result = Result.Fail<int>(ResultError.NotFound("Test", "x"));

        result.IsFailure.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccessResult()
    {
        Result<string> result = "hello";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void ImplicitConversion_FromError_CreatesFailureResult()
    {
        Result<string> result = ResultError.Conflict("Test", "duplicate");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ResultErrorCodes.Conflict);
    }

    [Fact]
    public void Match_CallsOnSuccess_WhenSuccessful()
    {
        var result = Result.Ok("hello");

        string projected = result.Match(
            onSuccess: v => v.ToUpperInvariant(),
            onFailure: _ => "error");

        projected.ShouldBe("HELLO");
    }

    [Fact]
    public void Match_CallsOnFailure_WhenFailed()
    {
        Result<string> result = ResultError.Validation("bad input");

        string projected = result.Match(
            onSuccess: _ => "ok",
            onFailure: e => e.Message);

        projected.ShouldBe("bad input");
    }
}
