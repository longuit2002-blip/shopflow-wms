namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Discriminated success/failure carrier for expected outcomes (oversold,
/// idempotency-key-reused, validation failures). Per AGENTS.md §4.21 domain
/// methods use this for expected failures and only throw for programmer
/// errors. Tech Design §20 verbatim, plus a non-generic helper type for
/// commands that have no return value.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? Error { get; }

    public string? ErrorCode { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private Result(string error, string? code)
    {
        IsSuccess = false;
        Error = error;
        ErrorCode = code;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(string error, string? code = null) => new(error, code);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

/// <summary>
/// Non-generic Result for commands without a payload.
/// </summary>
public sealed class Result
{
    public bool IsSuccess { get; }

    public string? Error { get; }

    public string? ErrorCode { get; }

    private Result(bool isSuccess, string? error, string? code)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = code;
    }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error, string? code = null) => new(false, error, code);
}
