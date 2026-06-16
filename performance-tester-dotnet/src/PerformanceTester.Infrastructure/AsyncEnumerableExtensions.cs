using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Minimal lazy operators over <see cref="IAsyncEnumerable{T}"/> for poll/retry pipelines.
/// Every element is produced only when the consumer pulls it, so a short-circuiting terminal
/// stops all upstream work (no further attempts, no further delays).
/// </summary>
internal static class AsyncEnumerableExtensions
{
    /// <summary>Yields at most <paramref name="count"/> elements, then stops (disposing the source).</summary>
    public static async IAsyncEnumerable<T> Take<T>(
        this IAsyncEnumerable<T> source,
        int count)
    {
        if (count <= 0)
        {
            yield break;
        }

        var taken = 0;
        await foreach (var item in source)
        {
            yield return item;

            if (++taken >= count)
            {
                yield break;
            }
        }
    }

    /// <summary>Projects each element through an asynchronous selector, sequentially (awaits before pulling the next).</summary>
    public static async IAsyncEnumerable<TResult> SelectAwait<TSource, TResult>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, Task<TResult>> selector)
    {
        await foreach (var item in source)
        {
            yield return await selector(item);
        }
    }

    /// <summary>
    /// Returns the first success (stopping immediately), otherwise the last failure seen.
    /// Throws <see cref="InvalidOperationException"/> if the sequence was empty — matching LINQ's <c>Last()</c>.
    /// </summary>
    public static async Task<Result<TSuccess, TFailure>> FirstSuccessOrLastAsync<TSuccess, TFailure>(
        this IAsyncEnumerable<Result<TSuccess, TFailure>> source,
        CancellationToken cancellationToken = default)
    {
        var sawAny = false;
        Result<TSuccess, TFailure> last = default!;

        await foreach (var result in source.WithCancellation(cancellationToken))
        {
            if (result is Result<TSuccess, TFailure>.Success)
            {
                return result;
            }

            last = result;
            sawAny = true;
        }

        if (!sawAny)
        {
            throw new InvalidOperationException("Sequence contained no attempts.");
        }

        return last;
    }
}
