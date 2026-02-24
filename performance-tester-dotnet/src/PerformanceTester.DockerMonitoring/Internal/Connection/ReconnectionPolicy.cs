using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Internal.Connection;

/// <summary>
/// Pure functions for reconnection backoff and retry decisions.
/// No side effects, no state — all inputs explicit, all outputs via return value.
/// </summary>
internal static class ReconnectionPolicy
{
    public readonly record struct BackoffResult(
        TimeSpan DelayToUse,
        TimeSpan NextBackoff);

    /// <summary>
    /// Calculates the current delay and the next backoff using exponential doubling with jitter.
    /// Returns <paramref name="currentBackoff"/> as the delay to use now,
    /// and min(currentBackoff * 2, maxBackoff) + jitter as the next backoff.
    /// </summary>
    public static BackoffResult CalculateBackoff(
        TimeSpan currentBackoff,
        TimeSpan maxBackoff) => CalculateBackoff(currentBackoff, maxBackoff, StreamingConstants.JitterMaxMilliseconds);

    public static BackoffResult CalculateBackoff(
        TimeSpan currentBackoff,
        TimeSpan maxBackoff,
        JitterMaxMilliseconds jitterMaxMs)
    {
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, jitterMaxMs.Value));
        var nextBackoff = TimeSpan.FromTicks(Math.Min(currentBackoff.Ticks * 2, maxBackoff.Ticks)) + jitter;
        return new(currentBackoff, nextBackoff);
    }

    /// <summary>
    /// Returns true if retrying is allowed (failures have not exceeded the limit).
    /// </summary>
    public static bool ShouldRetry(NonNegativeInt consecutiveFailures, NonNegativeInt maxAttempts) => consecutiveFailures <= maxAttempts;
}
