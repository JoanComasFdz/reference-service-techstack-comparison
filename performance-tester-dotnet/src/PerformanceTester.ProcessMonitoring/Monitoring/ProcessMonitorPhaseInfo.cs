using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Represents the phases in process monitoring lifecycle.
/// </summary>
public enum ProcessMonitorPhase
{
    /// <summary>Monitoring has been requested via StartMonitoringAsync().</summary>
    MonitoringRequested,

    /// <summary>First metrics sample has been collected.</summary>
    FirstSampleCollected,

    /// <summary>A sample has been collected (reported after each sample).</summary>
    SampleCollected,

    /// <summary>Process was not found during initial lookup.</summary>
    ProcessNotFound,

    /// <summary>Process has exited during monitoring.</summary>
    ProcessExited,

    /// <summary>Monitoring has stopped.</summary>
    MonitoringStopped
}

/// <summary>
/// Represents the state of a phase transition.
/// Aligns with DockerMonitorPhaseState and ConsumerPhaseState from other slices.
/// </summary>
public enum ProcessMonitorPhaseState
{
    /// <summary>Phase is about to start.</summary>
    Starting,

    /// <summary>Phase has completed successfully.</summary>
    Completed,

    /// <summary>Phase failed with an error.</summary>
    Failed
}

/// <summary>
/// Information about a phase transition in process monitoring.
/// Aligns with DockerMonitorPhaseInfo pattern from DockerMonitoring slice.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="ProcessId">ID of the process being monitored.</param>
/// <param name="SampleCount">Current total sample count.</param>
/// <param name="Message">Optional descriptive message.</param>
/// <param name="Timestamp">When the phase occurred.</param>
public readonly record struct ProcessMonitorPhaseInfo(
    ProcessMonitorPhase Phase,
    ProcessMonitorPhaseState State,
    ProcessId ProcessId,
    SampleCount SampleCount,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>Gets the timestamp, defaulting to now if not specified.</summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase is starting.</summary>
    public static ProcessMonitorPhaseInfo Starting(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Starting,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has completed.</summary>
    public static ProcessMonitorPhaseInfo Completed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Completed,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has completed with sample count.</summary>
    public static ProcessMonitorPhaseInfo Completed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        SampleCount sampleCount,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Completed,
        processId,
        sampleCount,
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has failed.</summary>
    public static ProcessMonitorPhaseInfo Failed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Failed,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);
}
