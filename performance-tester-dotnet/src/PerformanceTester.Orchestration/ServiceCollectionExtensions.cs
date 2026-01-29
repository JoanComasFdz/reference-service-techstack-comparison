using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.SystemMonitoring;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Extension methods for registering Orchestration services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers orchestration services and all dependencies (Phases 1-4).
    /// This is the single entry point for DI registration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="postgresConnectionString">PostgreSQL connection string</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ connection string</param>
    /// <param name="rabbitMqContainerName">RabbitMQ container name for monitoring (default: "performancetest-rabbitmq")</param>
    /// <param name="postgresContainerName">PostgreSQL container name for monitoring (default: "performancetest-postgres")</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method orchestrates registration of all performance testing infrastructure:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Phase 1: Infrastructure (ServiceDiscovery, Database, RabbitMQ)</description></item>
    /// <item><description>Phase 2: Data Collection (EventPublishing, EventConsuming, DockerMonitoring, ApiLoadTesting)</description></item>
    /// <item><description>Phase 3: Reporting (SystemInfo, ReportGenerator, ChartGenerator)</description></item>
    /// <item><description>Phase 4: Orchestration (TestOrchestrator)</description></item>
    /// </list>
    /// <para>
    /// ProcessMonitoring uses deferred start pattern - the process ID is provided after IHost.StartAsync()
    /// via IProcessMonitor.StartMonitoringAsync(processId). This allows the orchestrator to discover the process
    /// at runtime before beginning monitoring.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrchestration(
        this IServiceCollection services,
        string postgresConnectionString,
        string rabbitMqConnectionString,
        string rabbitMqContainerName = "performancetest-rabbitmq",
        string postgresContainerName = "performancetest-postgres")
    {
        // Phase 1: Infrastructure
        services.AddInfrastructure(
            postgresConnectionString: postgresConnectionString,
            rabbitMqConnectionString: rabbitMqConnectionString);

        // Phase 2: Data Collection Slices
        services.AddEventPublishing(rabbitMqConnectionString);
        services.AddEventConsuming(rabbitMqConnectionString);

        // Docker monitoring - register monitors for RabbitMQ and PostgreSQL
        // Orchestrator receives all monitors via IEnumerable<IDockerMonitor>
        services.AddDockerMonitoring(rabbitMqContainerName);
        services.AddDockerMonitoring(postgresContainerName);

        services.AddApiLoadTesting();

        // Process monitoring - deferred start pattern (no processId parameter)
        // The actual process ID is provided via StartMonitoringAsync() after host starts
        services.AddProcessMonitoring();

        // System-wide monitoring - deferred start pattern
        // Monitors CPU and memory at system level (not per-process)
        services.AddSystemMonitoring();

        // Phase 3: Reporting
        services.AddReporting();

        // Phase 4: Orchestration (self)
        services.AddSingleton<ITestOrchestrator, TestOrchestrator>();

        // Configure host options
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(60);
        });

        return services;
    }
}
