using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Docker.DotNet;
using Docker.DotNet.Models;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<string, PerformanceTester.DockerMonitoring.Stats.DockerError>;
using SnapshotResult = PerformanceTester.Functional.Result<Docker.DotNet.Models.ContainerStatsResponse, PerformanceTester.DockerMonitoring.Stats.DockerError>;

namespace PerformanceTester.DockerMonitoring.Stats;

/// <summary>
/// Pure static functions for all Docker API interactions.
/// Each function takes its dependencies as explicit parameters (DockerClient, cache).
/// Delegates in <see cref="StatsServiceCollectionExtensions"/> close over these dependencies.
/// </summary>
internal static class DockerOperations
{
    /// <summary>
    /// Resolves container name to container ID (cached).
    /// </summary>
    public static async Task<Result<string, DockerError>> GetContainerIdAsync(
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
                }, ct);

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
    public static async IAsyncEnumerable<ContainerStatsResponse> StreamStatsRawAsync(
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
    public static async Task<Result<ContainerStatsResponse, DockerError>> GetSnapshotAsync(
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
    /// Composes raw stats stream with <see cref="StatsProcessing.TryConvertToMetrics"/>,
    /// filtering out invalid stats (zeroed PreCPUStats).
    /// </summary>
    public static async IAsyncEnumerable<DockerMetrics> StreamMetrics(
        DockerClient client,
        string containerId,
        NonEmptyString containerName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var stats in StreamStatsRawAsync(client, containerId, ct))
        {
            var option = StatsProcessing.TryConvertToMetrics(stats, containerName, DateTime.UtcNow);
            if (option.IsSome)
            {
                yield return option.SomeValue;
            }
        }
    }
}
