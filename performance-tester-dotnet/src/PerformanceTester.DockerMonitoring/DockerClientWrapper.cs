using System.Runtime.InteropServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Wrapper around Docker.DotNet client for container stats retrieval.
/// </summary>
internal sealed class DockerClientWrapper : IDisposable
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerClientWrapper> _logger;

    public DockerClientWrapper(ILogger<DockerClientWrapper> logger)
    {
        _logger = logger;

        // Auto-detect Docker endpoint (Unix socket on Linux, named pipe on Windows)
        var dockerUri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        _client = new DockerClientConfiguration(dockerUri).CreateClient();

        _logger.LogDebug("Docker client initialized with endpoint: {Endpoint}", dockerUri);
    }

    /// <summary>
    /// Gets container stats snapshot (non-streaming).
    /// Returns null if container not found.
    /// </summary>
    public async Task<ContainerStatsResponse?> GetContainerStatsAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get container by name (matches Python behavior)
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = false, // Only running containers
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [containerName] = true }
                    }
                },
                cancellationToken);

            if (containers.Count == 0)
            {
                _logger.LogWarning("Container {ContainerName} not found", containerName);
                return null;
            }

            var containerId = containers[0].ID;

            // Get stats (stream=false for single snapshot)
            var statsProgress = new Progress<ContainerStatsResponse>();
            ContainerStatsResponse? result = null;

            statsProgress.ProgressChanged += (_, stats) => result = stats;

            await _client.Containers.GetContainerStatsAsync(
                containerId,
                new ContainerStatsParameters { Stream = false }, // Single snapshot
                statsProgress,
                cancellationToken);

            // Docker.DotNet invokes ProgressChanged asynchronously
            // Give it a moment to populate result
            await Task.Delay(100, cancellationToken);

            if (result == null)
            {
                _logger.LogWarning("Failed to get stats for container {ContainerName}", containerName);
            }

            return result;
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.LogWarning("Container {ContainerName} not found (DockerContainerNotFoundException)",
                containerName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stats for container {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Calculates CPU percentage from Docker stats.
    /// Formula: (cpu_delta / system_delta) * cpu_count * 100
    /// </summary>
    /// <remarks>
    /// <para><strong>Container-Level CPU Calculation</strong></para>
    /// <para>
    /// This method matches Docker's native stats API and Python reference implementation.
    /// It measures container CPU usage by comparing container CPU time against system-wide
    /// CPU time progression, scaled by available cores to show total capacity used.
    /// </para>
    /// <para><strong>Why This Differs from Process-Level CPU:</strong></para>
    /// <para>
    /// Container monitoring scales by multiplying by core count (total capacity).
    /// Process monitoring normalizes by dividing by core count (per-core average).
    /// See <c>ProcessCpuCalculator.Sample</c> in PerformanceTester.ProcessMonitoring
    /// for process-level CPU calculation. The formulas differ because containers use
    /// cgroup accounting (system CPU time) while processes use wall-clock time.
    /// </para>
    /// <para><strong>Stateless Design:</strong></para>
    /// <para>
    /// Unlike ProcessCpuCalculator, this method is stateless because Docker API provides
    /// both current stats (cpu_stats) and previous stats (precpu_stats) in a single response.
    /// No need to maintain state between calls.
    /// </para>
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

    public void Dispose()
    {
        _client?.Dispose();
    }
}
