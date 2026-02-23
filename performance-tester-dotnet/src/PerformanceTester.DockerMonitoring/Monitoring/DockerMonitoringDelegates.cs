using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

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
