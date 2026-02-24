using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

// ── Delegates ────────────────────────────────────────────────────────────────

/// <summary>
/// Reports docker monitoring phase changes.
/// Replaces IProgress&lt;DockerMonitorPhaseInfo&gt; with a named delegate per Guideline 02-02.
/// </summary>
public delegate void ReportDockerMonitorProgressDelegate(DockerMonitorPhaseInfo phaseInfo);

/// <summary>
/// Warms up Docker API for all registered containers.
/// First Docker API call is typically slow (~2-3s); this avoids measurement delays.
/// </summary>
public delegate Task WarmupDockerMonitorsDelegate(CancellationToken ct = default);

/// <summary>
/// Starts metrics collection on all registered containers.
/// Blocks until the first sample is collected per container.
/// </summary>
public delegate Task StartDockerMonitoringDelegate(
    ReportDockerMonitorProgressDelegate reportProgress,
    CancellationToken ct = default);

/// <summary>
/// Retrieves collected metrics for a specific container by name.
/// Returns metrics in chronological order.
/// </summary>
public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetricsDelegate(NonEmptyString containerName);

// ── Phase Info ───────────────────────────────────────────────────────────────

/// <summary>
/// Represents the phases in Docker container monitoring lifecycle.
/// These are high-level lifecycle phases, not internal implementation details.
/// </summary>
public enum DockerMonitorPhase
{
    /// <summary>
    /// Monitoring has been requested via StartMonitoringAsync().
    /// </summary>
    MonitoringRequested,

    /// <summary>
    /// Attempting to connect to Docker stats stream.
    /// </summary>
    StreamConnecting,

    /// <summary>
    /// Successfully connected and receiving stats from Docker.
    /// </summary>
    StreamConnected,

    /// <summary>
    /// Connection to Docker lost, will attempt reconnection.
    /// </summary>
    StreamDisconnected,

    /// <summary>
    /// Connection failed permanently (max retries exceeded, container not found, etc.).
    /// Check the Message property for details.
    /// </summary>
    StreamFailed,

    /// <summary>
    /// Monitoring completed successfully (normal shutdown).
    /// </summary>
    MonitoringCompleted
}

/// <summary>
/// Represents the state of a phase transition.
/// Aligns with ConsumerPhaseState from EventConsuming slice.
/// </summary>
public enum DockerMonitorPhaseState
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
/// Information about a phase transition in Docker monitoring.
/// Used with IProgress&lt;DockerMonitorPhaseInfo&gt; to notify observers.
/// Aligns with ConsumerPhaseInfo pattern from EventConsuming slice.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="ContainerName">Name of the container being monitored.</param>
/// <param name="SampleCount">Current total sample count.</param>
/// <param name="Message">Optional descriptive message.</param>
/// <param name="Timestamp">When the phase occurred.</param>
public readonly record struct DockerMonitorPhaseInfo(
    DockerMonitorPhase Phase,
    DockerMonitorPhaseState State,
    NonEmptyString ContainerName,
    SampleCount SampleCount,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>
    /// Gets the timestamp, defaulting to now if not specified.
    /// </summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase is starting.
    /// </summary>
    public static DockerMonitorPhaseInfo Starting(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        string? message = null)
    {
        return new(
            phase,
            DockerMonitorPhaseState.Starting,
            containerName,
            SampleCount.FromInt(0),
            message,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has completed.
    /// </summary>
    public static DockerMonitorPhaseInfo Completed(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        string? message = null)
    {
        return new(
            phase,
            DockerMonitorPhaseState.Completed,
            containerName,
            SampleCount.FromInt(0),
            message,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has completed.
    /// </summary>
    public static DockerMonitorPhaseInfo Completed(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        SampleCount sampleCount,
        string? message = null)
    {
        return new(
            phase,
            DockerMonitorPhaseState.Completed,
            containerName,
            sampleCount,
            message,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has failed.
    /// </summary>
    public static DockerMonitorPhaseInfo Failed(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        string? message = null)
    {
        return new(
            phase,
            DockerMonitorPhaseState.Failed,
            containerName,
            SampleCount.FromInt(0),
            message,
            DateTimeOffset.UtcNow);
    }
}

// ── Data Records ─────────────────────────────────────────────────────────────

/// <summary>
/// Represents Docker container metrics at a specific point in time.
/// </summary>
public sealed record DockerMetrics
{
    /// <summary>
    /// When the metrics were captured (UTC).
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Docker container ID (full SHA256).
    /// </summary>
    public required ContainerId ContainerId { get; init; }

    /// <summary>
    /// Human-readable container name.
    /// </summary>
    public required NonEmptyString ContainerName { get; init; }

    /// <summary>
    /// CPU usage percentage (0-100% per core, can exceed 100% on multi-core).
    /// Calculated as: (cpu_delta / system_delta) * cpu_count * 100
    /// </summary>
    public required CpuPercent CpuPercent { get; init; }

    /// <summary>
    /// Memory usage in megabytes.
    /// Calculated from stats.MemoryStats.Usage / 1024 / 1024.
    /// </summary>
    public required MemoryMB MemoryMB { get; init; }
}
