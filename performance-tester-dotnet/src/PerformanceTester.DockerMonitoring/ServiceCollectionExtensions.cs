using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Extension methods for registering Docker monitoring services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Docker container monitoring for a specific container.
    /// Registers a BackgroundService that samples container stats at regular intervals.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="containerName">Name of container to monitor (e.g., "performancetest-rabbitmq").</param>
    /// <param name="samplingInterval">How often to sample metrics (default: 500ms to match process monitoring).</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddDockerMonitoring(
        this IServiceCollection services,
        string containerName,
        TimeSpan? samplingInterval = null)
    {
        // Changed from 3000ms to 500ms (matches process monitoring)
        var interval = samplingInterval ?? TimeSpan.FromMilliseconds(500);

        // Register Docker client wrapper (shared across all container monitors)
        // Only register once if not already registered
        if (!services.Any(d => d.ServiceType == typeof(DockerClientWrapper)))
        {
            services.AddSingleton<DockerClientWrapper>();
        }

        // Register the monitor service as a keyed singleton
        // This ensures the same instance is used for both IHostedService and IDockerMonitor
        // without causing eager resolution of all IHostedService instances during Build()
        services.AddKeyedSingleton<DockerMonitorService>(containerName, (sp, key) =>
        {
            var dockerClient = sp.GetRequiredService<DockerClientWrapper>();
            var logger = sp.GetRequiredService<ILogger<DockerMonitorService>>();

            return new DockerMonitorService(
                containerName,
                interval,
                dockerClient,
                logger);
        });

        // Register as IHostedService (retrieves the keyed singleton)
        services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredKeyedService<DockerMonitorService>(containerName));

        // Register as IDockerMonitor (retrieves the same keyed singleton)
        services.AddSingleton<IDockerMonitor>(sp =>
            sp.GetRequiredKeyedService<DockerMonitorService>(containerName));

        return services;
    }
}
