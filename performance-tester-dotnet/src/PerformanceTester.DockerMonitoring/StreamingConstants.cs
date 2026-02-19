using PerformanceTester.DockerMonitoring.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Constants for Docker streaming mode configuration.
/// </summary>
internal static class StreamingConstants
{
    /// <summary>
    /// Timeout waiting for first stats from Docker stream.
    /// Docker typically pushes first stats within 1-2 seconds.
    /// </summary>
    public static readonly TimeSpan FirstStatsTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for waiting on streaming task during shutdown.
    /// </summary>
    public static readonly TimeSpan StreamingShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initial delay before first reconnection attempt.
    /// </summary>
    public static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between reconnection attempts.
    /// </summary>
    public static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum consecutive failures before giving up.
    /// Resets to 0 when connection succeeds (first valid stats received).
    /// </summary>
    public static readonly AttemptCount MaxReconnectAttempts = AttemptCount.FromInt(10);
}
