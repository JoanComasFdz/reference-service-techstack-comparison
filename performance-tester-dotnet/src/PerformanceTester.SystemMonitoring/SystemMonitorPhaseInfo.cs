namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Phase information for system monitor progress reporting.
/// </summary>
public enum SystemMonitorPhase
{
    /// <summary>Monitoring has been requested but not yet started.</summary>
    MonitoringRequested,

    /// <summary>First sample has been collected.</summary>
    FirstSampleCollected,

    /// <summary>A regular sample has been collected.</summary>
    SampleCollected,

    /// <summary>Monitoring has been stopped.</summary>
    MonitoringStopped
}

/// <summary>
/// Progress information for system monitor phases.
/// </summary>
/// <param name="Phase">Current phase.</param>
/// <param name="IsCompleted">Whether the phase is completed.</param>
/// <param name="IsFailed">Whether the phase failed with an error.</param>
/// <param name="SampleCount">Number of samples collected so far.</param>
/// <param name="Message">Optional message.</param>
public sealed record SystemMonitorPhaseInfo(
    SystemMonitorPhase Phase,
    bool IsCompleted,
    bool IsFailed = false,
    int SampleCount = 0,
    string? Message = null)
{
    /// <summary>
    /// Creates a starting phase info.
    /// </summary>
    public static SystemMonitorPhaseInfo Starting(SystemMonitorPhase phase, string? message = null) =>
        new(phase, false, false, 0, message);

    /// <summary>
    /// Creates a completed phase info.
    /// </summary>
    public static SystemMonitorPhaseInfo Completed(SystemMonitorPhase phase, int sampleCount = 0, string? message = null) =>
        new(phase, true, false, sampleCount, message);

    /// <summary>
    /// Creates a failed phase info.
    /// </summary>
    public static SystemMonitorPhaseInfo Failed(SystemMonitorPhase phase, int sampleCount = 0, string? message = null) =>
        new(phase, false, true, sampleCount, message);
}
