namespace SmartLedger.Application.Common;

public enum ErrorType
{
    Validation,
    Unauthorized,
    NotFound,
    Conflict,

    /// <summary>A service we depend on is down; trying again later may work.</summary>
    Unavailable,
}

/// <summary>An expected failure (wrong password, duplicate email…) returned instead of thrown.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}

/// <summary>
/// Either a value or an <see cref="Error"/>. Services return this for outcomes the caller must handle,
/// keeping exceptions for real bugs and outages.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        Error = error;
    }

    public bool IsSuccess { get; }

    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value; check IsSuccess first.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);
}
