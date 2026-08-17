namespace MangaStore.Application.Common;

/// <summary>Represents the outcome of an operation that carries no value.</summary>
public class Result
{
    /// <param name="isSuccess"><see langword="true"/> for a successful outcome.</param>
    /// <param name="error">Must be <see cref="ResultError.None"/> on success and a real error on failure.</param>
    protected Result(bool isSuccess, ResultError error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the error associated with a failed result; <see cref="ResultError.None"/> on success.</summary>
    public ResultError Error { get; }

    /// <summary>Creates a successful result with no value.</summary>
    public static Result Ok() => new(true, ResultError.None);

    /// <summary>Creates a failed result with the given error.</summary>
    /// <param name="error">The error that describes the failure.</param>
    public static Result Fail(ResultError error) => new(false, error);

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    public static Result<TValue> Ok<TValue>(TValue value) => new(value, true, ResultError.None);

    /// <summary>Creates a failed result of type <typeparamref name="TValue"/> with the given error.</summary>
    /// <param name="error">The error that describes the failure.</param>
    public static Result<TValue> Fail<TValue>(ResultError error) => new(default, false, error);
}

/// <summary>Represents the outcome of an operation that produces a value of type <typeparamref name="TValue"/>.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, ResultError error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>Gets the value produced by a successful operation.</summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed on a failed result.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed result.");

    /// <summary>Projects the result into a single value by applying one of two functions.</summary>
    /// <typeparam name="TOut">The projected type.</typeparam>
    /// <param name="onSuccess">Called with the wrapped value when the result is successful.</param>
    /// <param name="onFailure">Called with the error when the result is a failure.</param>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<ResultError, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    /// <summary>Implicitly wraps a value in a successful result.</summary>
    public static implicit operator Result<TValue>(TValue value) => Ok(value);

    /// <summary>Implicitly wraps an error in a failed result.</summary>
    public static implicit operator Result<TValue>(ResultError error) => Fail<TValue>(error);
}
