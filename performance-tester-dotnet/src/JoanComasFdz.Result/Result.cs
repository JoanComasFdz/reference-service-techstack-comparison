using System;

namespace JoanComasFdz.Result;

/// <summary>
/// A discriminated union representing either a successful value or a string failure message.
/// </summary>
public abstract record Result<TValue>
{
    private Result() { }

    public sealed record Success(TValue Value) : Result<TValue>;
    public sealed record Failure(string Message) : Result<TValue>;

    public T Match<T>(Func<Success, T> success, Func<Failure, T> failure) =>
        this switch
        {
            Success s => success(s),
            Failure f => failure(f),
            _ => throw new InvalidOperationException("Unreachable")
        };

    public void Match(Action<Success> success, Action<Failure> failure)
    {
        switch (this)
        {
            case Success s: success(s); break;
            case Failure f: failure(f); break;
        }
    }
}

/// <summary>
/// A discriminated union representing either a successful value or a typed failure.
/// </summary>
public abstract record Result<TValue, TFailure>
{
    private Result() { }

    public sealed record Success(TValue Value) : Result<TValue, TFailure>;
    public sealed record Failure(TFailure Error) : Result<TValue, TFailure>;

    public T Match<T>(Func<Success, T> success, Func<Failure, T> failure) =>
        this switch
        {
            Success s => success(s),
            Failure f => failure(f),
            _ => throw new InvalidOperationException("Unreachable")
        };

    public void Match(Action<Success> success, Action<Failure> failure)
    {
        switch (this)
        {
            case Success s: success(s); break;
            case Failure f: failure(f); break;
        }
    }
}
