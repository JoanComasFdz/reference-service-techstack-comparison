using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Docker.DotNet;
using Microsoft.Extensions.DependencyInjection;

namespace PerformanceTester.DockerMonitoring.Stats;

/// <summary>
/// Composition root for low-level Docker operations.
/// Creates DockerClient once and registers delegate singletons that close over it.
/// Cache is a private closure variable — only <see cref="GetContainerIdDelegate"/> and
/// <see cref="InvalidateContainerCacheDelegate"/> know it exists.
/// </summary>
internal static class StatsServiceCollectionExtensions
{
    internal static IServiceCollection AddDockerStats(this IServiceCollection services)
    {
        var uri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        var client = new DockerClientConfiguration(uri).CreateClient();
        var cache = new ConcurrentDictionary<string, string>();

        // Register via factory so DI container owns disposal
        services.AddSingleton<DockerClient>(_ => client);

        services.AddSingleton<GetContainerIdDelegate>(_ => (name, ct) => DockerOperations.GetContainerIdAsync(client, cache, name, ct));

        services.AddSingleton<StreamStatsRawDelegate>(_ => (id, ct) => DockerOperations.StreamStatsRawAsync(client, id, ct));

        services.AddSingleton<GetSnapshotDelegate>(_ => (id, ct) => DockerOperations.GetSnapshotAsync(client, id, ct));

        services.AddSingleton<StreamMetricsDelegate>(_ => (id, name, ct) => DockerOperations.StreamMetrics(client, id, name, ct));

        services.AddSingleton<InvalidateContainerCacheDelegate>(sp => name => { cache.TryRemove(name, out _); });

        return services;
    }
}
