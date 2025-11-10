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
    /// <param name="samplingInterval">How often to sample metrics (default: 3000ms).</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddDockerMonitoring(
        this IServiceCollection services,
        string containerName,
        TimeSpan? samplingInterval = null)
    {
        var interval = samplingInterval ?? TimeSpan.FromMilliseconds(3000);

        // Register Docker client wrapper (shared across all container monitors)
        // Only register once if not already registered
        if (!services.Any(d => d.ServiceType == typeof(DockerClientWrapper)))
        {
            services.AddSingleton<DockerClientWrapper>();
        }

        // Create the monitor instance as a singleton
        // This ensures the same instance is used for both IHostedService and IDockerMonitor
        services.AddSingleton<IHostedService>(sp =>
        {
            var dockerClient = sp.GetRequiredService<DockerClientWrapper>();
            var logger = sp.GetRequiredService<ILogger<DockerMonitorService>>();

            return new DockerMonitorService(
                containerName,
                interval,
                dockerClient,
                logger);
        });

        // Register as IDockerMonitor (same instance as IHostedService)
        services.AddSingleton<IDockerMonitor>(sp =>
        {
            // Find the IHostedService that matches this container name
            var hostedServices = sp.GetServices<IHostedService>();
            var monitorService = hostedServices
                .OfType<DockerMonitorService>()
                .FirstOrDefault(m => m.ContainerName == containerName);

            if (monitorService != null)
            {
                return monitorService;
            }

            // If not found, create a new one (shouldn't happen if registration order is correct)
            var dockerClient = sp.GetRequiredService<DockerClientWrapper>();
            var logger = sp.GetRequiredService<ILogger<DockerMonitorService>>();

            return new DockerMonitorService(
                containerName,
                interval,
                dockerClient,
                logger);
        });

        return services;
    }
}
