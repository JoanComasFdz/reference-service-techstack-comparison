namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Represents the phases in Docker container monitoring lifecycle.
/// </summary>
public enum DockerMonitorPhase
{
    /// <summary>
    /// Monitoring has been requested via StartMonitoringAsync().
    /// </summary>
    MonitoringRequested,

    /// <summary>
    /// First metrics sample has been collected.
    /// </summary>
    FirstSampleCollected,

    /// <summary>
    /// A sample has been collected (reported after each sample).
    /// </summary>
    SampleCollected,

    /// <summary>
    /// Container was not found during sampling.
    /// </summary>
    ContainerNotFound,

    /// <summary>
    /// Monitoring has stopped.
    /// </summary>
    MonitoringStopped
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
/// <param name="SampleCount">Current total sample count (for SampleCollected phase).</param>
/// <param name="Message">Optional descriptive message.</param>
/// <param name="Timestamp">When the phase occurred.</param>
public readonly record struct DockerMonitorPhaseInfo(
    DockerMonitorPhase Phase,
    DockerMonitorPhaseState State,
    string ContainerName,
    int SampleCount = 0,
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
        string containerName,
        int sampleCount = 0,
        string? message = null)
        => new(phase, DockerMonitorPhaseState.Starting, containerName, sampleCount, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has completed.
    /// </summary>
    public static DockerMonitorPhaseInfo Completed(
        DockerMonitorPhase phase,
        string containerName,
        int sampleCount = 0,
        string? message = null)
        => new(phase, DockerMonitorPhaseState.Completed, containerName, sampleCount, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a DockerMonitorPhaseInfo indicating a phase has failed.
    /// </summary>
    public static DockerMonitorPhaseInfo Failed(
        DockerMonitorPhase phase,
        string containerName,
        int sampleCount = 0,
        string? message = null)
        => new(phase, DockerMonitorPhaseState.Failed, containerName, sampleCount, message, DateTimeOffset.UtcNow);
}
