using PerformanceTester.Functional;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure.Database;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Shared named delegates used across multiple test phases.
/// </summary>
internal static class PhasesToolbox
{
    /// <summary>
    /// Clears all data from the target database.
    /// Returns Unit on success, or a ClearDatabaseError on failure.
    /// </summary>
    public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase();

    /// <summary>
    /// Purges all RabbitMQ queues and waits for consumer recovery.
    /// Returns Unit on success, or an error message on failure.
    /// </summary>
    public delegate Task<Result<Unit, string>> ClearAllQueues();

    /// <summary>
    /// Publishes the specified number of events and returns publish metrics.
    /// </summary>
    public delegate Task<PublishMetrics> PublishEvents(int eventCount);

    /// <summary>
    /// Tracks consumed events until expected count is reached or inactivity timeout expires.
    /// </summary>
    public delegate Task TrackEvents(int expectedCount, TimeSpan inactivityTimeout, IProgress<ConsumerPhaseInfo>? progress);
}
