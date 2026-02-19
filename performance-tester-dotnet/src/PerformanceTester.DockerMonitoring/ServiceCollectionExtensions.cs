using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.Stats;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Extension methods for registering Docker monitoring services.
/// Registers BackgroundServices internally and exposes named delegates publicly.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Docker container monitoring for the RabbitMQ and PostgreSQL containers.
    /// Registers internal BackgroundServices and three public named delegates:
    /// <see cref="WarmupDockerMonitors"/>, <see cref="StartDockerMonitoring"/>,
    /// and <see cref="GetDockerMetrics"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="rabbitMqContainerName">RabbitMQ container name to monitor.</param>
    /// <param name="postgresContainerName">PostgreSQL container name to monitor.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddDockerMonitoring(
        this IServiceCollection services,
        RabbitMqContainerName rabbitMqContainerName,
        PostgresContainerName postgresContainerName)
    {
        NonEmptyString[] containerNames = [rabbitMqContainerName, postgresContainerName];
        // Shared Docker client (singleton, registered once)
        services.AddSingleton<DockerClientWrapper>();

        // Register one BackgroundService per container (keyed singletons)
        containerNames.ToList().ForEach(name =>
        {
            services.AddKeyedSingleton<DockerMonitorService>(name.Value, (sp, _) =>
                new DockerMonitorService(
                    name,
                    sp.GetRequiredService<DockerClientWrapper>(),
                    sp.GetRequiredService<ILogger<DockerMonitorService>>()));

            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<DockerMonitorService>(name.Value));
        });

        // Helper: resolve all monitors from DI
        IEnumerable<DockerMonitorService> resolveMonitors(IServiceProvider sp) =>
            containerNames.Select(name =>
                sp.GetRequiredKeyedService<DockerMonitorService>(name.Value));

        // Register the 3 public named delegates
        services.AddSingleton<WarmupDockerMonitors>(sp =>
            ct => Task.WhenAll(resolveMonitors(sp).Select(m => m.WarmupAsync(ct))));

        services.AddSingleton<StartDockerMonitoring>(sp =>
            (progress, ct) => Task.WhenAll(
                resolveMonitors(sp).Select(m => m.StartMonitoringAsync(progress, ct))));

        services.AddSingleton<GetDockerMetrics>(sp =>
            containerName => resolveMonitors(sp)
                .Single(m => m.ContainerName == containerName.Value)
                .GetCollectedMetrics());

        return services;
    }
}
