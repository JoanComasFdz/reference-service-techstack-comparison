using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.SystemMonitoring;

using PerformanceTester.Orchestration.Internal;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Extension methods for registering Orchestration services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers orchestration services and all dependencies (Phases 1-3).
    /// This is the single entry point for DI registration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="postgresConnectionString">PostgreSQL connection string</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ connection string</param>
    /// <param name="rabbitMqContainerName">RabbitMQ container name for monitoring.</param>
    /// <param name="postgresContainerName">PostgreSQL container name for monitoring.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method orchestrates registration of all performance testing infrastructure:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Phase 1: Infrastructure (ServiceDiscovery, Database, RabbitMQ)</description></item>
    /// <item><description>Phase 2: Data Collection (EventPublishing, EventConsuming, DockerMonitoring, ApiLoadTesting)</description></item>
    /// <item><description>Phase 3: Reporting (SystemInfo, ReportGenerator, ChartGenerator)</description></item>
    /// </list>
    /// <para>
    /// ProcessMonitoring uses deferred start pattern - the process ID is provided after IHost.StartAsync()
    /// via StartProcessMonitoringDelegate. This allows the orchestrator to discover the process
    /// at runtime before beginning monitoring.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrchestration(
        this IServiceCollection services,
        string postgresConnectionString,
        string rabbitMqConnectionString,
        RabbitMqContainerName rabbitMqContainerName,
        PostgresContainerName postgresContainerName)
    {
        // Phase 1: Infrastructure
        services.AddInfrastructure(
            postgresConnectionString: postgresConnectionString,
            rabbitMqConnectionString: rabbitMqConnectionString);

        // Phase 2: Data Collection Slices
        services.AddEventPublishing(rabbitMqConnectionString);
        services.AddEventConsuming(rabbitMqConnectionString);

        // Docker monitoring - register monitors and expose named delegates
        services.AddDockerMonitoring(rabbitMqContainerName, postgresContainerName);

        services.AddApiLoadTesting();

        // Process monitoring - deferred start pattern (no processId parameter)
        // The actual process ID is provided via StartMonitoringAsync() after host starts
        services.AddProcessMonitoring();

        // System-wide monitoring - deferred start pattern
        // Monitors CPU and memory at system level (not per-process)
        services.AddSystemMonitoring();

        // Phase 3: Reporting
        services.AddReporting();

        // Configure host options
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(60);
        });

        // Public delegate wrapping internal orchestrator (Guideline 05-06: consumers never using .Internal)
        services.AddSingleton<RunPerformanceTestDelegate>(sp =>
        {
            return (services, config, reportProgress, logger, ct) =>
            {
                var deps = TestOrchestrator.BuildDependencies(services, config, reportProgress, logger, ct);
                return TestOrchestrator.RunTestAsync(deps, config, logger);
            };
        });

        return services;
    }
}
