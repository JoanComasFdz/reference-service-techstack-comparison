using System.Collections.Concurrent;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Centralizes all mutable state for process monitoring (Guideline 32).
/// Passed explicitly to static operations — no hidden fields.
/// </summary>
internal sealed record MonitorContext
{
    /// <summary>Thread-safe collection of all sampled metrics.</summary>
    public ConcurrentBag<ProcessMetrics> CollectedMetrics { get; } = new();

    /// <summary>Signal from StartMonitoringAsync → ExecuteAsync (deferred start).</summary>
    public TaskCompletionSource StartSignal { get; } = new();

    /// <summary>Signal from sampling loop → StartMonitoringAsync (first sample collected).</summary>
    public TaskCompletionSource FirstSampleCollected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Cached process name extracted from command line (set once, read many).</summary>
    public string? CachedProcessName { get; set; }
}
