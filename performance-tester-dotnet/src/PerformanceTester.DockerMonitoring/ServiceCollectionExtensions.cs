using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.Internal;
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
    /// <see cref="WarmupDockerMonitorsDelegate"/>, <see cref="StartDockerMonitoringDelegate"/>,
    /// and <see cref="GetDockerMetricsDelegate"/>.
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

        // Build Docker stats dependencies (DockerClient + cache, captured in closures)
        var statsDeps = StatsModule.BuildDependencies();

        // Register one BackgroundService per container (keyed singletons)
        containerNames.ToList().ForEach(name =>
        {
            services.AddKeyedSingleton<DockerMonitorBackgroundService>(
                name.Value,
                (sp, _) => new DockerMonitorBackgroundService(
                    name,
                    statsDeps,
                    sp.GetRequiredService<ILogger<DockerMonitorBackgroundService>>()));

            services.AddSingleton<IHostedService>(
                sp => sp.GetRequiredKeyedService<DockerMonitorBackgroundService>(name.Value));
        });

        IEnumerable<DockerMonitorBackgroundService> resolveMonitors(IServiceProvider sp) => containerNames
            .Select(name => sp.GetRequiredKeyedService<DockerMonitorBackgroundService>(name.Value));

        // Register the 3 public named delegates
        services.AddSingleton<WarmupDockerMonitorsDelegate>(
            sp => ct => Task.WhenAll(resolveMonitors(sp).Select(m => m.WarmupAsync(ct))));

        services.AddSingleton<StartDockerMonitoringDelegate>(
            sp => (progress, ct) => Task.WhenAll(
                resolveMonitors(sp).Select(m => m.StartMonitoringAsync(progress, ct))));

        services.AddSingleton<GetDockerMetricsDelegate>(
            sp => containerName => resolveMonitors(sp)
                .Single(m => m.ContainerName == containerName.Value)
                .GetCollectedMetrics());

        return services;
    }
}
