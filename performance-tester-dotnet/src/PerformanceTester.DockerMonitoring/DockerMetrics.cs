using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

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
    public required double CpuPercent { get; init; }

    /// <summary>
    /// Memory usage in megabytes.
    /// Calculated from stats.MemoryStats.Usage / 1024 / 1024.
    /// </summary>
    public required double MemoryMB { get; init; }
}
