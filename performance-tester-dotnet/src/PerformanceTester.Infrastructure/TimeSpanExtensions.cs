using System.Runtime.CompilerServices;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Extension methods over <see cref="TimeSpan"/>.
/// </summary>
internal static class TimeSpanExtensions
{
    /// <summary>
    /// Produces an infinite lazy stream that emits 0 immediately, then 1, 2, … each spaced by this
    /// interval. The delay sits <em>after</em> each yield, so it only elapses when the consumer pulls
    /// the next element — pairing with a short-circuiting terminal to stop all upstream work.
    /// Honors cancellation before each emission and during each delay (preserving the old poll's
    /// "throw before the first attempt on an already-cancelled token" contract).
    /// </summary>
    public static async IAsyncEnumerable<int> Tick(
        this TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var n = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return n++;
            await Task.Delay(interval, cancellationToken);
        }
    }
}
