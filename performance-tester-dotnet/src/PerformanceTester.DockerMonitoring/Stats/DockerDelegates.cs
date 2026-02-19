using Docker.DotNet.Models;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Stats;

/// <summary>
/// Resolves a container name to its Docker container ID (cached).
/// Returns <see cref="Result{TSuccess,TFailure}.Failure"/> if the container is not found or an error occurs.
/// </summary>
internal delegate Task<Result<string, DockerError>> GetContainerIdDelegate(
    string containerName,
    CancellationToken ct = default);

/// <summary>
/// Streams raw Docker stats as an async enumerable.
/// Bridges Docker.DotNet's IProgress callback to IAsyncEnumerable via Channel.
/// </summary>
internal delegate IAsyncEnumerable<ContainerStatsResponse> StreamStatsRawDelegate(
    string containerId,
    CancellationToken ct);

/// <summary>
/// Gets a single container stats snapshot (non-streaming).
/// </summary>
internal delegate Task<Result<ContainerStatsResponse, DockerError>> GetSnapshotDelegate(
    string containerId,
    CancellationToken ct = default);

/// <summary>
/// Streams validated Docker metrics as an async enumerable.
/// Composes raw stats with <see cref="StatsProcessing.TryConvertToMetrics"/>,
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
