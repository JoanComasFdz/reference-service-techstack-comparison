using System;

namespace PerformanceTester.Functional;

/// <summary>
/// LINQ query syntax support for <see cref="Result{TSuccess,TFailure}"/>.
/// Enables railway-oriented programming via <c>from...in...select</c> comprehension syntax.
/// <para>
/// <b>Example:</b>
/// <code>
/// var result =
///     from count    in EventCount.Create(options.Events)
///     from workers  in WorkerCount.Create(options.Workers)
///     select (count, workers);
/// </code>
/// The chain short-circuits on the first failure.
/// </para>
/// </summary>
public static class ResultLinqExtensions
{
    /// <summary>
    /// Projects the success value. Enables the <c>select</c> keyword in LINQ queries.
    /// </summary>
    public static Result<U, TError> Select<T, TError, U>(
        this Result<T, TError> result,
        Func<T, U> selector)
    {
        if (result is Result<T, TError>.Success s)
        {
            return new Result<U, TError>.Success(selector(s.Value));
        }

        return new Result<U, TError>.Failure(((Result<T, TError>.Failure)result).Error);
    }

    /// <summary>
    /// Chains a dependent operation. Enables multiple <c>from</c> clauses in LINQ queries.
    /// </summary>
    public static Result<V, TError> SelectMany<T, TError, U, V>(
        this Result<T, TError> result,
        Func<T, Result<U, TError>> bind,
        Func<T, U, V> project)
    {
        if (result is not Result<T, TError>.Success s)
        {
            return new Result<V, TError>.Failure(((Result<T, TError>.Failure)result).Error);
        }

        var bound = bind(s.Value);
        if (bound is Result<U, TError>.Success next)
        {
            return new Result<V, TError>.Success(project(s.Value, next.Value));
        }

        return new Result<V, TError>.Failure(((Result<U, TError>.Failure)bound).Error);
    }
}
