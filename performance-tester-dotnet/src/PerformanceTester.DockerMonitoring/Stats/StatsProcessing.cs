using Docker.DotNet.Models;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Option<PerformanceTester.DockerMonitoring.DockerMetrics>;

namespace PerformanceTester.DockerMonitoring.Stats;

/// <summary>
/// Pure functions for converting Docker stats responses into domain metrics.
/// Moved from DockerClientWrapper (HasValidPreCpuStats, CalculateCpuPercent)
/// plus new TryConvertToMetrics composition.
/// </summary>
internal static class StatsProcessing
{
    /// <summary>
    /// Converts a raw Docker stats response into a <see cref="DockerMetrics"/> if the stats are valid.
    /// Returns None when PreCPUStats are invalid (first stats push from Docker has zeroed values).
    /// </summary>
    public static Option<DockerMetrics> TryConvertToMetrics(
        ContainerStatsResponse stats,
        NonEmptyString containerName,
        DateTime timestamp)
    {
        if (!HasValidPreCpuStats(stats))
        {
            return new None();
        }

        return new Some(new DockerMetrics
        {
            Timestamp = timestamp,
            ContainerId = ContainerId.FromString(stats.ID),
            ContainerName = containerName,
            CpuPercent = CpuPercent.FromDouble(CalculateCpuPercent(stats)),
            MemoryMB = MemoryMB.FromDouble(Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2))
        });
    }

    /// <summary>
    /// Validates that ContainerStatsResponse has valid PreCPUStats for CPU calculation.
    /// The first stats from a stream often have zeroed PreCPUStats.
    /// </summary>
    public static bool HasValidPreCpuStats(ContainerStatsResponse stats) => stats.PreCPUStats.SystemUsage > 0;

    /// <summary>
    /// Calculates CPU percentage from Docker stats.
    /// Formula: (cpu_delta / system_delta) * cpu_count * 100
    /// </summary>
    /// <remarks>
    /// Matches Docker CLI (<c>docker stats</c>) and Python reference implementation.
    /// Stateless: Docker API provides both current and previous stats in a single response.
    /// </remarks>
    public static double CalculateCpuPercent(ContainerStatsResponse stats)
    {
        var cpuDelta = stats.CPUStats.CPUUsage.TotalUsage -
                       stats.PreCPUStats.CPUUsage.TotalUsage;

        var systemDelta = stats.CPUStats.SystemUsage -
                          stats.PreCPUStats.SystemUsage;

        var cpuCount = stats.CPUStats.OnlineCPUs;

        if (systemDelta > 0 && cpuDelta > 0)
        {
            var cpuPercent = (double)cpuDelta / systemDelta * cpuCount * 100.0;
            return Math.Round(cpuPercent, 2);
        }

        return 0.0;
    }
}
