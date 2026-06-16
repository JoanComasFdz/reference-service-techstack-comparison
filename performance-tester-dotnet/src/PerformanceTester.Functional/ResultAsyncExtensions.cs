using System;
using System.Threading.Tasks;

namespace PerformanceTester.Functional;

/// <summary>
/// Railway combinators for <see cref="Result{TSuccess,TFailure}"/> that thread through <see cref="Task"/>
/// and operate on the failure side. Synchronous map/bind already exist as <c>Select</c>/<c>SelectMany</c>
/// in <see cref="ResultLinqExtensions"/>; these add the async-threading and side-effecting variants.
/// </summary>
public static class ResultAsyncExtensions
{
    /// <summary>
    /// Turns a <c>Success</c> into a <c>Failure</c> when <paramref name="predicate"/> does not hold.
    /// A <c>Failure</c> passes through unchanged (the predicate is not evaluated).
    /// </summary>
    public static Result<T, E> Ensure<T, E>(
        this Result<T, E> result,
        Func<T, bool> predicate,
        Func<T, E> error)
    {
        if (result is Result<T, E>.Success success && !predicate(success.Value))
        {
            return new Result<T, E>.Failure(error(success.Value));
        }

        return result;
    }

    /// <summary>
    /// Chains an asynchronous dependent operation onto a synchronous <see cref="Result{T,E}"/>.
    /// Short-circuits on <c>Failure</c>.
    /// </summary>
    public static async Task<Result<U, E>> BindAsync<T, U, E>(
        this Result<T, E> result,
        Func<T, Task<Result<U, E>>> bind)
    {
        if (result is Result<T, E>.Success success)
        {
            return await bind(success.Value);
        }

        return new Result<U, E>.Failure(((Result<T, E>.Failure)result).Error);
    }

    /// <summary>Runs <paramref name="onSuccess"/> for its side effect when the awaited result is a success; the value flows through unchanged.</summary>
    public static async Task<Result<T, E>> Tap<T, E>(
        this Task<Result<T, E>> resultTask,
        Action<T> onSuccess)
    {
        var result = await resultTask;
        if (result is Result<T, E>.Success success)
        {
            onSuccess(success.Value);
        }

        return result;
    }

    /// <summary>Runs <paramref name="onFailure"/> for its side effect when the awaited result is a failure; the value flows through unchanged.</summary>
    public static async Task<Result<T, E>> TapError<T, E>(
        this Task<Result<T, E>> resultTask,
        Action<E> onFailure)
    {
        var result = await resultTask;
        if (result is Result<T, E>.Failure failure)
        {
            onFailure(failure.Error);
        }

        return result;
    }

    /// <summary>Rewrites the failure value of an awaited result; a success passes through unchanged.</summary>
    public static async Task<Result<T, F>> MapError<T, E, F>(
        this Task<Result<T, E>> resultTask,
        Func<E, F> map)
    {
        var result = await resultTask;
        if (result is Result<T, E>.Success success)
        {
            return new Result<T, F>.Success(success.Value);
        }

        return new Result<T, F>.Failure(map(((Result<T, E>.Failure)result).Error));
    }
}
