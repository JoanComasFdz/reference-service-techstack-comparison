namespace PerformanceTester.Reporting;

/// <summary>
/// Complete test report aggregating all metrics and configuration.
/// Matches Python report_generator.py output format.
/// </summary>
public sealed record TestReport
{
    /// <summary>
    /// Test execution timestamp (UTC).
    /// </summary>
    public required DateTime TestDate { get; init; }

    /// <summary>
    /// Total test runtime in seconds (from start to final metric collected).
    /// </summary>
    public required double TotalRuntimeSeconds { get; init; }

    /// <summary>
    /// Phase timing information (start/end timestamps for each phase).
    /// </summary>
    public required PhaseTimestamps PhaseTimestamps { get; init; }

    /// <summary>
    /// Monitored process information.
    /// </summary>
    public required MonitoredProcess MonitoredProcess { get; init; }

    /// <summary>
    /// System hardware and OS information.
    /// </summary>
    public required SystemInfo System { get; init; }

    /// <summary>
    /// Test configuration (event count, API duration, etc.).
    /// </summary>
    public required TestConfiguration Configuration { get; init; }

    /// <summary>
    /// Aggregated test results (throughput, resource usage).
    /// </summary>
    public required TestResults Results { get; init; }

    // Sample Data Collections (from Phase 2 monitoring)

    /// <summary>
    /// Event throughput samples collected during Phase 2 (consume).
    /// Sampling interval: 100ms.
    /// </summary>
    public IReadOnlyList<ThroughputMetricSample> EventsThroughputSamples { get; init; } = Array.Empty<ThroughputMetricSample>();

    /// <summary>
    /// API throughput samples collected during Phase 3 (API load test).
    /// Sampling interval: 100ms.
    /// </summary>
    public IReadOnlyList<ThroughputMetricSample> ApiThroughputSamples { get; init; } = Array.Empty<ThroughputMetricSample>();

    /// <summary>
    /// Process resource samples for the monitored service.
    /// Sampling interval: 500ms.
    /// Includes threads field and uses memory_rss_mb.
    /// </summary>
    public IReadOnlyList<ProcessResourceSample> ProcessResourceSamples { get; init; } = Array.Empty<ProcessResourceSample>();

    /// <summary>
    /// System-wide resource samples (all processes).
    /// Sampling interval: 500ms.
    /// </summary>
    public IReadOnlyList<ContainerResourceSample> SystemResourceSamples { get; init; } = Array.Empty<ContainerResourceSample>();

    /// <summary>
    /// RabbitMQ container resource samples.
    /// Sampling interval: 3000ms.
    /// </summary>
    public IReadOnlyList<ContainerResourceSample> RabbitMqResourceSamples { get; init; } = Array.Empty<ContainerResourceSample>();

    /// <summary>
    /// PostgreSQL container resource samples.
    /// Sampling interval: 3000ms.
    /// </summary>
    public IReadOnlyList<ContainerResourceSample> PostgresResourceSamples { get; init; } = Array.Empty<ContainerResourceSample>();
}

/// <summary>
/// Phase timing information (elapsed seconds from test start).
/// </summary>
public sealed record PhaseTimestamps
{
    /// <summary>
    /// Phase 1 (publish) start time (always 0.0).
    /// </summary>
    public required double Phase1Start { get; init; }

    /// <summary>
    /// Phase 1 (publish) end time.
    /// </summary>
    public required double Phase1End { get; init; }

    /// <summary>
    /// Phase 2 (consume) start time (typically same as Phase1End).
    /// </summary>
    public required double Phase2Start { get; init; }

    /// <summary>
    /// Phase 2 (consume) end time.
    /// </summary>
    public required double Phase2End { get; init; }

    /// <summary>
    /// Phase 3 (API load test) start time.
    /// </summary>
    public required double Phase3Start { get; init; }

    /// <summary>
    /// Phase 3 (API load test) end time.
    /// </summary>
    public required double Phase3End { get; init; }
}

/// <summary>
/// Information about the monitored service process.
/// </summary>
public sealed record MonitoredProcess
{
    /// <summary>
    /// Process name (e.g., "goReferenceService").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Process ID (PID).
    /// </summary>
    public required int Pid { get; init; }
}

/// <summary>
/// Test configuration parameters.
/// </summary>
public sealed record TestConfiguration
{
    /// <summary>
    /// Number of events published and consumed.
    /// </summary>
    public required int NumEvents { get; init; }

    /// <summary>
    /// API load test duration (e.g., "30s", "2m").
    /// </summary>
    public required string ApiDuration { get; init; }

    /// <summary>
    /// Number of concurrent API workers (k6 VUs).
    /// </summary>
    public required int ApiConcurrentWorkers { get; init; }

    /// <summary>
    /// RabbitMQ exchange name for publishing.
    /// </summary>
    public required string RabbitmqExchange { get; init; }

    /// <summary>
    /// RabbitMQ consumer queue name.
    /// </summary>
    public required string ConsumerQueue { get; init; }

    /// <summary>
    /// API endpoint URL tested.
    /// </summary>
    public required string ApiEndpoint { get; init; }

    /// <summary>
    /// Published event type (CloudEvent type field).
    /// </summary>
    public required string PublishEventType { get; init; }

    /// <summary>
    /// Consumed event type (CloudEvent type field).
    /// </summary>
    public required string ConsumeEventType { get; init; }
}

/// <summary>
/// Aggregated test results for all phases.
/// </summary>
public sealed record TestResults
{
    /// <summary>
    /// Phase 1 (publish events) results.
    /// </summary>
    public required PublishResults Phase1Publish { get; init; }

    /// <summary>
    /// Phase 2 (consume events) results.
    /// </summary>
    public required ConsumeResults Phase2Consume { get; init; }

    /// <summary>
    /// Phase 3 (API load test) results.
    /// </summary>
    public required ApiResults Phase3Api { get; init; }
}

/// <summary>
/// Phase 1 publish results.
/// </summary>
public sealed record PublishResults
{
    /// <summary>
    /// Publish duration in seconds.
    /// </summary>
    public required double DurationSeconds { get; init; }

    /// <summary>
    /// Average publish throughput (events/second).
    /// </summary>
    public required double ThroughputEventsPerSec { get; init; }
}

/// <summary>
/// Phase 2 consume results.
/// </summary>
public sealed record ConsumeResults
{
    /// <summary>
    /// Consume duration in seconds.
    /// </summary>
    public required double DurationSeconds { get; init; }

    /// <summary>
    /// Average consume throughput (events/second).
    /// </summary>
    public required double ThroughputEventsPerSec { get; init; }
}

/// <summary>
/// Phase 3 API load test results.
/// </summary>
public sealed record ApiResults
{
    /// <summary>
    /// API test duration in seconds.
    /// </summary>
    public required double DurationSeconds { get; init; }

    /// <summary>
    /// Total API requests made.
    /// </summary>
    public required int TotalRequests { get; init; }

    /// <summary>
    /// Average API throughput (requests/second).
    /// </summary>
    public required double ThroughputCallsPerSec { get; init; }

    /// <summary>
    /// Successful API requests count.
    /// </summary>
    public required int SuccessCount { get; init; }

    /// <summary>
    /// Success percentage (0-100).
    /// </summary>
    public required double SuccessPercentage { get; init; }

    /// <summary>
    /// Failed API requests count.
    /// </summary>
    public required int ErrorCount { get; init; }

    /// <summary>
    /// Error percentage (0-100).
    /// </summary>
    public required double ErrorPercentage { get; init; }

    /// <summary>
    /// True if API load test was aborted early due to consecutive failures.
    /// </summary>
    public bool WasAborted { get; init; } = false;

    /// <summary>
    /// Reason for abort, if test was aborted.
    /// </summary>
    public string? AbortReason { get; init; }
}
