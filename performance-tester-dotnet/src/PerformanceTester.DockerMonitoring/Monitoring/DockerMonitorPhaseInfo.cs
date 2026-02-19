using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

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
        => new(phase, DockerMonitorPhaseState.Starting, containerName,
            SampleCount.FromInt(0), message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has completed.
    /// </summary>
    public static DockerMonitorPhaseInfo Completed(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        string? message = null)
        => new(phase, DockerMonitorPhaseState.Completed, containerName,
            SampleCount.FromInt(0), message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has completed.
    /// </summary>
    public static DockerMonitorPhaseInfo Completed(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        SampleCount sampleCount,
        string? message = null)
        => new(phase, DockerMonitorPhaseState.Completed, containerName,
            sampleCount, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has failed.
    /// </summary>
    public static DockerMonitorPhaseInfo Failed(
        DockerMonitorPhase phase,
        NonEmptyString containerName,
        string? message = null)
        => new(phase, DockerMonitorPhaseState.Failed, containerName,
            SampleCount.FromInt(0), message, DateTimeOffset.UtcNow);
}
