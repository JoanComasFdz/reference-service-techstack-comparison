using PerformanceTester.IntegrationTesting;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for ProcessMonitoring integration tests.
/// Extends base System class (from Phase 0) and adds ProcessMonitoring facade for accessing production services.
/// Provides access to shared infrastructure helpers (PostgreSQL, RabbitMQ) if needed in future.
/// </summary>
/// <remarks>
/// NOTE: ProcessMonitoring facade is created per-test via CreateProcessMonitoring() because:
/// - Each test needs different sampling intervals (100ms, 50ms, 500ms)
/// - Each test needs fresh metrics collection (no shared state)
/// - This follows the same pattern as EventConsuming which needs unique queue names per test
/// </remarks>
public sealed class ProcessMonitoringSystem : IntegrationTesting.System
{
    /// <summary>
    /// ProcessMonitoring facade providing access to all ProcessMonitoring services via DI.
    /// Accessed as: System.ProcessMonitoring.Monitor
    /// Created per-test via CreateProcessMonitoring() with test-specific parameters.
    /// </summary>
    public ProcessMonitoring ProcessMonitoring { get; private set; } = null!;

    /// <summary>
    /// Creates ProcessMonitoring facade with specified process ID and sampling interval.
    /// Call this in test Arrange phase with test-specific parameters.
    /// </summary>
    /// <param name="processId">Process ID to monitor (typically Environment.ProcessId for self-monitoring).</param>
    /// <param name="samplingInterval">Sampling interval for metrics collection (default: 500ms).</param>
    /// <remarks>
    /// This method must be called per-test because each test needs:
    /// - Different sampling intervals to test various scenarios
    /// - Fresh metrics collection with no shared state
    /// - Clean BackgroundService lifecycle (Start/Stop per test)
    /// </remarks>
    public void CreateProcessMonitoring(int processId, TimeSpan? samplingInterval = null)
    {
        ProcessMonitoring = new ProcessMonitoring(processId, samplingInterval, base.Output);
    }

    public override void Dispose()
    {
        ProcessMonitoring?.Dispose();
        base.Dispose();
    }
}
