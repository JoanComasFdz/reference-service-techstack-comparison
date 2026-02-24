using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Docker.DotNet;
using Docker.DotNet.Models;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Option<PerformanceTester.DockerMonitoring.DockerMetrics>;
using static PerformanceTester.Functional.Result<string, PerformanceTester.DockerMonitoring.Internal.StatsModule.DockerError>;
using SnapshotResult = PerformanceTester.Functional.Result<Docker.DotNet.Models.ContainerStatsResponse, PerformanceTester.DockerMonitoring.Internal.StatsModule.DockerError>;

namespace PerformanceTester.DockerMonitoring.Internal;

/// <summary>
/// FP-style module for low-level Docker operations (Guideline 02-05).
/// Owns delegate definitions, Dependencies record, factory, and all Docker API operations.
/// Reads top-to-bottom: types → delegates → bundle → factory → operations → processing.
/// </summary>
internal static class StatsModule
{
    // ── Types ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Error type for Docker operations. Used as TFailure in Result&lt;T, DockerError&gt;.
    /// </summary>
    internal sealed record DockerError(string Message, Exception? Cause = null);

    // ── Delegate Definitions ─────────────────────────────────────────────────

    /// <summary>
    /// Resolves a container name to its Docker container ID (cached).
    /// Returns <see cref="Result{TSuccess,TFailure}.Failure"/> if the container is not found or an error occurs.
    /// </summary>
    internal delegate Task<Result<string, DockerError>> GetContainerIdDelegate(
        string containerName,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a single container stats snapshot (non-streaming).
    /// </summary>
    internal delegate Task<Result<ContainerStatsResponse, DockerError>> GetSnapshotDelegate(
        string containerId,
        CancellationToken ct = default);

    /// <summary>
    /// Streams validated Docker metrics as an async enumerable.
    /// Composes raw stats with <see cref="TryConvertToMetrics"/>,
    /// filtering out invalid stats (zeroed PreCPUStats).
    /// </summary>
    internal delegate IAsyncEnumerable<DockerMetrics> StreamMetricsDelegate(
        string containerId,
        NonEmptyString containerName,
        CancellationToken ct);

    /// <summary>
    /// Invalidates cached container ID (call on reconnection to handle container restarts).
    /// </summary>
    internal delegate void InvalidateContainerCacheDelegate(string containerName);

    // ── Dependencies Record ──────────────────────────────────────────────────

    /// <summary>
    /// Bundles all Docker operation delegates needed by consumers.
    /// Built once by <see cref="BuildDependencies"/> and passed to <see cref="DockerMonitorBackgroundService"/>.
    /// </summary>
    internal record Dependencies(
        GetContainerIdDelegate GetContainerId,
        GetSnapshotDelegate GetSnapshot,
        StreamMetricsDelegate StreamMetrics,
        InvalidateContainerCacheDelegate InvalidateCache);

    // ── Factory ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates Dependencies by constructing a DockerClient and container ID cache.
    /// The client and cache are captured in closures — callers never see them.
    /// Platform-aware: uses Unix socket on Linux/macOS, named pipe on Windows.
    /// </summary>
    internal static Dependencies BuildDependencies()
    {
        var uri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        var client = new DockerClientConfiguration(uri).CreateClient();
        var cache = new ConcurrentDictionary<string, string>();

        return new Dependencies(
            GetContainerId: (name, ct) => GetContainerIdAsync(client, cache, name, ct),
            GetSnapshot: (id, ct) => GetSnapshotAsync(client, id, ct),
            StreamMetrics: (id, name, ct) => StreamMetricsAsync(client, id, name, ct),
            InvalidateCache: name => { cache.TryRemove(name, out _); });
    }

    // ── Operations ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves container name to container ID (cached).
    /// </summary>
    private static async Task<Result<string, DockerError>> GetContainerIdAsync(
        DockerClient client,
        ConcurrentDictionary<string, string> cache,
        string containerName,
        CancellationToken ct = default)
    {
        if (cache.TryGetValue(containerName, out var cached))
        {
            return new Success(cached);
        }

        try
        {
            var containers = await client.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = false,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [containerName] = true }
                    }
                },
                ct);

            if (containers.Count == 0)
            {
                return new Failure(new DockerError($"Container '{containerName}' not found"));
            }

            var id = containers[0].ID;
            cache[containerName] = id;
            return new Success(id);
        }
        catch (Exception ex)
        {
            return new Failure(new DockerError($"Error resolving '{containerName}'", ex));
        }
    }

    /// <summary>
    /// Streams raw Docker stats as an async enumerable.
    /// Bridges Docker.DotNet's IProgress callback to IAsyncEnumerable via Channel.
    /// </summary>
    private static async IAsyncEnumerable<ContainerStatsResponse> StreamStatsRawAsync(
        DockerClient client,
        string containerId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ContainerStatsResponse>(new UnboundedChannelOptions { SingleWriter = true });

        var progress = new Progress<ContainerStatsResponse>(stats => channel.Writer.TryWrite(stats));

        _ = client.Containers
            .GetContainerStatsAsync(
                containerId,
                new ContainerStatsParameters { Stream = true },
                progress,
                ct)
            .ContinueWith(_ => channel.Writer.Complete(), CancellationToken.None);

        await foreach (var stats in channel.Reader.ReadAllAsync(ct))
        {
            yield return stats;
        }
    }

    /// <summary>
    /// Gets a single container stats snapshot (non-streaming).
    /// Uses TaskCompletionSource to bridge Docker.DotNet's IProgress callback.
    /// </summary>
    private static async Task<Result<ContainerStatsResponse, DockerError>> GetSnapshotAsync(
        DockerClient client,
        string containerId,
        CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ContainerStatsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        var progress = new Progress<ContainerStatsResponse>(stats => tcs.TrySetResult(stats));

        try
        {
            await client.Containers.GetContainerStatsAsync(
                containerId,
                new ContainerStatsParameters { Stream = false },
                progress,
                ct);

            return new SnapshotResult.Success(await tcs.Task.WaitAsync(ct));
        }
        catch (Exception ex)
        {
            return new SnapshotResult.Failure(
                new DockerError($"Snapshot failed for '{containerId[..12]}'", ex));
        }
    }

    /// <summary>
    /// Streams validated Docker metrics as an async enumerable.
    /// Composes raw stats stream with <see cref="TryConvertToMetrics"/>,
    /// filtering out invalid stats (zeroed PreCPUStats).
    /// </summary>
    private static async IAsyncEnumerable<DockerMetrics> StreamMetricsAsync(
        DockerClient client,
        string containerId,
        NonEmptyString containerName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var stats in StreamStatsRawAsync(client, containerId, ct))
        {
            var option = TryConvertToMetrics(stats, containerName, DateTime.UtcNow);
            if (option.IsSome)
            {
                yield return option.SomeValue;
            }
        }
    }

    // ── Stats Processing ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a raw Docker stats response into a <see cref="DockerMetrics"/> if the stats are valid.
    /// Returns None when PreCPUStats are invalid (first stats push from Docker has zeroed values).
    /// </summary>
    private static Option<DockerMetrics> TryConvertToMetrics(
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
    private static bool HasValidPreCpuStats(ContainerStatsResponse stats) => stats.PreCPUStats.SystemUsage > 0;

    /// <summary>
    /// Calculates CPU percentage from Docker stats.
    /// Formula: (cpu_delta / system_delta) * cpu_count * 100
    /// </summary>
    /// <remarks>
    /// Matches Docker CLI (<c>docker stats</c>) and Python reference implementation.
    /// Stateless: Docker API provides both current and previous stats in a single response.
    /// </remarks>
    private static double CalculateCpuPercent(ContainerStatsResponse stats)
    {
        var cpuDelta = stats.CPUStats.CPUUsage.TotalUsage -
                       stats.PreCPUStats.CPUUsage.TotalUsage;

        var systemDelta = stats.CPUStats.SystemUsage -
                          stats.PreCPUStats.SystemUsage;

        if (systemDelta <= 0 || cpuDelta <= 0)
        {
            return 0.0;
        }

        var cpuCount = stats.CPUStats.OnlineCPUs;
        return Math.Round((double)cpuDelta / systemDelta * cpuCount * 100.0, 2);
    }
}
