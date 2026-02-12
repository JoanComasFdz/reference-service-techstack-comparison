using JoanComasFdz.Result;
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
}
