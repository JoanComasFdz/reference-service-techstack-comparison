using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Internal aggregation of test results from all phases.
/// Used to build the final TestReport.
/// </summary>
internal record TestResult
{
    // Test metadata
    public required Guid TestRunId { get; init; }
    public required DateTime TestStartTime { get; init; }
    public required DateTime TestEndTime { get; init; }
    public required TestConfiguration Configuration { get; init; }

    // Service information
    public required int ServiceProcessId { get; init; }
    public required string ServiceProcessName { get; init; }

    // Phase timestamps
    public required DateTime WarmupStartTime { get; init; }
    public required DateTime WarmupEndTime { get; init; }
    public required DateTime EventTestStartTime { get; init; }
    public required DateTime EventTestEndTime { get; init; }
    public required DateTime ApiTestStartTime { get; init; }
    public required DateTime ApiTestEndTime { get; init; }

    // Phase results
    public required PublishMetrics PublishMetrics { get; init; }
    public required ApiLoadTestResult ApiLoadTestResult { get; init; }

    // Collected samples
    public required IReadOnlyCollection<EventThroughputSample> ThroughputSamples { get; init; }
    public required IReadOnlyCollection<ProcessMetrics> ProcessMetrics { get; init; }
    public required IReadOnlyCollection<DockerMetrics> RabbitMqMetrics { get; init; }
    public required IReadOnlyCollection<DockerMetrics> PostgresMetrics { get; init; }

    // System information
    public required SystemInfo? SystemInfo { get; init; }
}
