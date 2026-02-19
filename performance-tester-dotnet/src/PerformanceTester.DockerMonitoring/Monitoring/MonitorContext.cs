using System.Collections.Concurrent;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

/// <summary>
/// All mutable state for a single container monitor session.
/// Owned by DockerMonitorService, passed explicitly to static MonitoringOperations functions.
/// </summary>
internal sealed record MonitorContext(NonEmptyString ContainerName)
{
    public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
    public TaskCompletionSource StartSignal { get; } = new();
    public TaskCompletionSource FirstSampleCollected { get; } = new();
    public TaskCompletionSource FirstValidStatsReceived { get; } = new();
    public bool StreamingFailed { get; set; }
}
