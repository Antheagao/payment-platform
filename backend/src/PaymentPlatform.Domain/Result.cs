using System.Diagnostics.CodeAnalysis;

namespace PaymentPlatform.Domain;

/// <summary>The outcome of an operation that returns nothing on success.</summary>
public readonly record struct Result
{
    private Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static implicit operator Result(DomainError error) => Failure(error);
}

/// <summary>The outcome of an operation that returns a <typeparamref name="T"/> on success.</summary>
public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(T? value, DomainError? error)
    {
        _value = value;
        Error = error;
    }

    public DomainError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The value; reading it from a failed result is a programming error.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result failed with '{Error.Code}' and has no value.");

    // Construction is by implicit conversion from a value or an error, which keeps call sites short
    // (`return money;`, `return error;`) and avoids static factories on a generic type (CA1000).
    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}
