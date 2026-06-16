using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// A bounded poll loop expressed as a lazy pipeline: run an attempt, and while it fails, wait
/// <c>interval</c> and try again — up to <c>⌈timeout / interval⌉</c> attempts (always at least one).
/// The attempt arrives as a function (a higher-order step, like a LINQ predicate — not an injected
/// dependency). No wall-clock is read: the attempt count is derived once from the two durations, so the
/// number of attempts is independent of how long any single attempt takes.
/// </summary>
internal static class Poll
{
    /// <summary>
    /// Runs <paramref name="attempt"/> at most <c>⌈timeout / interval⌉</c> times, spaced by
    /// <paramref name="interval"/>, returning the first success or — if none succeed — the last failure.
    /// </summary>
    public static Task<Result<T, string>> UntilSuccessOrTimeoutAsync<T>(
        Func<CancellationToken, Task<Result<T, string>>> attempt,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, (int)Math.Ceiling(timeout / interval));

        return interval.Tick(cancellationToken)
            .Take(maxAttempts)
            .SelectAwait(_ => attempt(cancellationToken))
            .FirstSuccessOrLastAsync(cancellationToken);
    }
}
