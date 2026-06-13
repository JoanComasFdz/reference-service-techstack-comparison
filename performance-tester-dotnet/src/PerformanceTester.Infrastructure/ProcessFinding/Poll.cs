using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// A generic poll loop: repeatedly invokes an attempt at a fixed interval until it succeeds or the
/// timeout elapses. This is the impure shell — it reads the clock and sleeps — but it knows nothing
/// about <em>what</em> is being polled. The attempt arrives as a function (a higher-order step, like a
/// LINQ predicate — not an injected dependency).
/// </summary>
internal static class Poll
{
    /// <summary>
    /// Runs <paramref name="attempt"/> every <paramref name="interval"/> until it returns a success or
    /// <paramref name="timeout"/> elapses. On timeout, the last failure is returned.
    /// </summary>
    public static async Task<Result<T, string>> UntilSuccessOrTimeoutAsync<T>(
        Func<CancellationToken, Task<Result<T, string>>> attempt,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await attempt(cancellationToken);
            if (result.IsSuccess)
            {
                return result;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return result;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }
}
