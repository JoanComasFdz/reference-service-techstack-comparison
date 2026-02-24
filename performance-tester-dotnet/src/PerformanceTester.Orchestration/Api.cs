using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ValueObjects;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.Orchestration;

// =====================================================================
// Delegates (Guideline 05-06 reading order: delegates first)
// =====================================================================

/// <summary>
/// Reports a phase transition to the progress display.
/// Non-nullable: callers that have no observer pass a no-op lambda.
/// </summary>
public delegate void ReportPhaseProgressDelegate(PhaseInfo phaseInfo);

/// <summary>
/// Runs a complete performance test: builds phase-level delegates from DI,
/// sequences execution through all phases, and returns the final report.
/// This is the public entry point to the orchestration workflow.
/// Consumers use this delegate instead of accessing internal types directly.
/// </summary>
public delegate Task<Result<TestReport, TestRunFailure>> RunPerformanceTestDelegate(
    IServiceProvider services,
    TestConfiguration config,
    ReportPhaseProgressDelegate reportProgress,
    ILogger logger,
    CancellationToken ct = default);

// =====================================================================
// Phase info — enums and record struct
// =====================================================================

/// <summary>
/// Represents the test phases in the orchestration workflow.
/// </summary>
public enum TestPhase
{
    /// <summary>
    /// Setup phase: service discovery, infrastructure initialization.
    /// </summary>
    Setup,

    /// <summary>
    /// Warmup phase: non-measured warmup events and API calls.
    /// </summary>
    Warmup,

    /// <summary>
    /// Event test phase: measured publish/consume throughput test.
    /// </summary>
    EventTest,

    /// <summary>
    /// API test phase: measured HTTP load test.
    /// </summary>
    ApiTest,

    /// <summary>
    /// Teardown phase: disconnect event publisher, stop monitoring services.
    /// </summary>
    Teardown,

    /// <summary>
    /// Reporting phase: metrics collection, report generation.
    /// </summary>
    Reporting
}

/// <summary>
/// Represents the state of a phase transition.
/// </summary>
public enum PhaseState
{
    /// <summary>
    /// Phase is about to start.
    /// </summary>
    Starting,

    /// <summary>
    /// Phase has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Phase failed with an error.
    /// </summary>
    Failed
}

/// <summary>
/// Information about a phase transition in the test workflow.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="Message">Optional descriptive message about the phase.</param>
/// <param name="Timestamp">When the transition occurred.</param>
public readonly record struct PhaseInfo(
    TestPhase Phase,
    PhaseState State,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>
    /// Gets the timestamp, defaulting to now if not specified.
    /// </summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a PhaseInfo indicating a phase is starting.
    /// </summary>
    public static PhaseInfo Starting(TestPhase phase, string? message = null) => new(
        phase,
        PhaseState.Starting,
        message,
        DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a PhaseInfo indicating a phase has completed.
    /// </summary>
    public static PhaseInfo Completed(TestPhase phase, string? message = null) => new(
        phase,
        PhaseState.Completed,
        message,
        DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a PhaseInfo indicating a phase has failed.
    /// </summary>
    public static PhaseInfo Failed(TestPhase phase, string? message = null) => new(
        phase,
        PhaseState.Failed,
        message,
        DateTimeOffset.UtcNow);
}

// =====================================================================
// Data records
// =====================================================================

/// <summary>
/// Represents a managed (non-exceptional) failure of a test run,
/// identifying which phase failed and why.
/// </summary>
public sealed record TestRunFailure(TestPhase Phase, string Message);

/// <summary>
/// Configuration for a complete performance test run.
/// </summary>
/// <param name="EventCount">Number of events to publish and consume</param>
/// <param name="ApiDuration">Duration of API load test (parsed from CLI string like "30s")</param>
/// <param name="ApiWorkers">Number of concurrent API workers</param>
/// <param name="InactivityTimeout">Timeout for consumer inactivity, parsed from duration string (e.g., 120s, 2m)</param>
/// <param name="WarmupEventCount">Number of events for warmup phase (0-10,000)</param>
/// <param name="WarmupApiCallCount">Number of HTTP calls to make during API warmup (0-1,000)</param>
/// <param name="WarmupInactivityTimeout">Timeout for consumer inactivity during warmup, parsed from duration string (e.g., 30s, 1m)</param>
/// <param name="ServicePort">Port where service is running (1-65535)</param>
/// <param name="DatabaseName">PostgreSQL database name for the service (non-empty)</param>
/// <param name="ResultsFolder">Directory to save test results (may be created if it doesn't exist)</param>
/// <param name="RabbitMqContainerName">Name of RabbitMQ Docker container (non-empty)</param>
/// <param name="PostgresContainerName">Name of PostgreSQL Docker container (non-empty)</param>
/// <param name="MaxConsecutiveApiFailures">Maximum consecutive API failures before aborting load test (default: 3)</param>
public record TestConfiguration(
    EventCount EventCount,
    ApiDuration ApiDuration,
    WorkerCount ApiWorkers,
    Port ServicePort,
    WarmupEventsCount WarmupEventCount,
    WarmupApiCallsCount WarmupApiCallCount,
    DatabaseName DatabaseName,
    ResultsOutputFolder ResultsFolder,
    RabbitMqContainerName RabbitMqContainerName,
    PostgresContainerName PostgresContainerName,
    InactivityTimeout InactivityTimeout,
    InactivityTimeout WarmupInactivityTimeout,
    MaxConsecutiveFailures MaxConsecutiveApiFailures)
{
    /// <summary>
    /// Gets the API URL based on the service port.
    /// </summary>
    public ServiceUrl ApiUrl => ServiceUrl.FromString($"http://localhost:{ServicePort.Value}/kpi");
}
