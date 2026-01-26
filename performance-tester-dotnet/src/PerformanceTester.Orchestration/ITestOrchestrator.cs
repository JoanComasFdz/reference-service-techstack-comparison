using PerformanceTester.Reporting;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Orchestrates a complete performance test workflow including:
/// - Infrastructure setup and cleanup
/// - Warmup phase
/// - Event throughput testing (concurrent publish/consume)
/// - API load testing
/// - Metrics collection
/// - Report generation
/// </summary>
public interface ITestOrchestrator
{
    /// <summary>
    /// Runs a complete performance test with the specified configuration.
    /// </summary>
    /// <param name="configuration">Test configuration parameters</param>
    /// <param name="progress">Optional progress reporter for phase transitions.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete test report with all metrics and analysis</returns>
    /// <exception cref="TimeoutException">Service not found on port within timeout</exception>
    /// <exception cref="InvalidOperationException">Test phase failed</exception>
    Task<TestReport> RunTestAsync(
        TestConfiguration configuration,
        IProgress<PhaseInfo>? progress = null,
        CancellationToken cancellationToken = default);
}
