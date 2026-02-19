using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

/// <summary>
/// Reports docker monitoring phase changes.
/// Replaces IProgress&lt;DockerMonitorPhaseInfo&gt; with a named delegate per Guideline 13.
/// </summary>
public delegate void ReportDockerMonitorProgress(DockerMonitorPhaseInfo phaseInfo);

/// <summary>
/// Warms up Docker API for all registered containers.
/// First Docker API call is typically slow (~2-3s); this avoids measurement delays.
/// </summary>
public delegate Task WarmupDockerMonitors(CancellationToken ct = default);

/// <summary>
/// Starts metrics collection on all registered containers.
/// Blocks until the first sample is collected per container.
/// </summary>
public delegate Task StartDockerMonitoring(
    ReportDockerMonitorProgress reportProgress,
    CancellationToken ct = default);

/// <summary>
/// Retrieves collected metrics for a specific container by name.
/// Returns metrics in chronological order.
/// </summary>
public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetrics(NonEmptyString containerName);
