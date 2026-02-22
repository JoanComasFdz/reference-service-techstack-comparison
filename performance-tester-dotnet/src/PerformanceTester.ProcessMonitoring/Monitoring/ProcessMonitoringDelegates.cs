using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Reports process monitoring phase changes.
/// Replaces IProgress&lt;ProcessMonitorPhaseInfo&gt; with a named delegate per Guideline 13.
/// </summary>
public delegate void ReportProcessMonitorProgressDelegate(ProcessMonitorPhaseInfo phaseInfo);

/// <summary>
/// Starts process resource monitoring for the given PID.
/// Blocks until the first sample has been collected.
/// </summary>
public delegate Task StartProcessMonitoringDelegate(
    ProcessId processId,
    ReportProcessMonitorProgressDelegate reportProgress,
    CancellationToken ct = default);

/// <summary>
/// Returns all collected process metrics in chronological order.
/// Call after test completion (after stopping IHost).
/// </summary>
public delegate IReadOnlyCollection<ProcessMetrics> GetProcessMetricsDelegate();
