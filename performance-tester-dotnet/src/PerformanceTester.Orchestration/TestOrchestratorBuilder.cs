using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ChartGeneration;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.SystemMonitoring;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds <see cref="OrchestratorDeps"/> from DI-resolved interfaces.
/// This is the ONLY place in the codebase that interfaces are converted to phase delegates.
/// </summary>
internal static class TestOrchestratorBuilder
{
    public static OrchestratorDeps Build(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var serviceDiscovery = services.GetRequiredService<IServiceDiscovery>();
        var database = services.GetRequiredService<IDatabase>();
        var rabbitMq = services.GetRequiredService<IRabbitMQ>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();
        var eventConsumer = services.GetRequiredService<IEventConsumer>();
        var metricsCollector = services.GetRequiredService<IMetricsCollector>();
        var processMonitor = services.GetRequiredService<IProcessMonitor>();
        var systemMonitor = services.GetRequiredService<ISystemMonitor>();
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
        var apiLoadTester = services.GetRequiredService<IApiLoadTester>();
        var systemInfoDetector = services.GetRequiredService<ISystemInfoDetector>();
        var reportGenerator = services.GetRequiredService<ReportGenerator>();
        var host = services.GetRequiredService<IHost>();
        var hostLifetime = services.GetRequiredService<IHostApplicationLifetime>();

        // Shared operation-level delegates (reused across phases)
        PhasesToolbox.ClearDatabase clearDatabase = () =>
            database.ClearDatabaseAsync(config.DatabaseName.Value, ct);

        PhasesToolbox.ClearAllQueues clearAllQueues = async () =>
        {
            var result = await rabbitMq.ClearAllQueuesAsync(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            return result;
        };

        PhasesToolbox.PublishEvents publishEvents = (count) =>
            eventPublisher.PublishEventsAsync(count, ct);

        PhasesToolbox.TrackEvents trackEvents = (count, timeout, progress) =>
            eventConsumer.StartTrackingEventsAsync(count, timeout, progress, ct);

        Task forAllDockerMonitors(Func<IDockerMonitor, Task> action) =>
            Task.WhenAll(dockerMonitors.Select(action));

        return new OrchestratorDeps(
            RunSetup: (testRunId) => SetupPhase.ExecuteAsync(
                testRunId,
                findServiceProcessId: () => serviceDiscovery.FindServiceProcessIdAsync(
                    config.ServicePort.Value, TimeSpan.FromSeconds(30), ct),
                isMonitoringStarted: () => hostLifetime.ApplicationStarted.IsCancellationRequested,
                startMonitoring: () => host.StartAsync(ct),
                warmupDockerApi: () => forAllDockerMonitors(m => m.WarmupAsync(ct)),
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                connectEventPublisher: () => eventPublisher.ConnectAsync(ct),
                logger),

            RunWarmup: () => WarmupPhase.ExecuteAsync(
                config,
                trackEvents: trackEvents,
                publishEvents: publishEvents,
                executeWarmupApiCalls: (url, count) =>
                    WarmupPhase.ExecuteWarmupApiCallsAsync(url, count, logger, ct),
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                logger),

            RunEventTest: (serviceProcessId, progress) => EventTestPhase.ExecuteAsync(
                config, serviceProcessId,
                systemCpuCount: systemMonitor.CpuCount,
                systemIsWsl2: systemMonitor.IsWsl2,
                clearSamples: metricsCollector.ClearSamples,
                startProcessMonitoring: (pid) =>
                    processMonitor.StartMonitoringAsync(pid, cancellationToken: ct),
                startSystemMonitoring: () =>
                    systemMonitor.StartMonitoringAsync(cancellationToken: ct),
                startDockerMonitoring: () =>
                    forAllDockerMonitors(m => m.StartMonitoringAsync(cancellationToken: ct)),
                trackEvents: trackEvents,
                publishEvents: publishEvents,
                progress, logger),

            RunApiTest: (progress) => ApiTestPhase.ExecuteAsync(
                config,
                (url, duration, vus, apiProgress, maxFail, dir) =>
                    apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, ct),
                progress, logger),

            RunReporting: (testResult) => ReportingPhase.ExecuteAsync(
                testResult, config,
                getThroughputSamples: metricsCollector.GetThroughputSamples,
                getProcessMetrics: processMonitor.GetCollectedMetrics,
                getSystemMetrics: systemMonitor.GetCollectedMetrics,
                getRabbitMqMetrics: () => dockerMonitors
                    .Single(m => m.ContainerName == config.RabbitMqContainerName.Value)
                    .GetCollectedMetrics(),
                getPostgresMetrics: () => dockerMonitors
                    .Single(m => m.ContainerName == config.PostgresContainerName.Value)
                    .GetCollectedMetrics(),
                getSystemInfo: () => systemInfoDetector.GetSystemInfoAsync(ct),
                generateReport: (folder, report) =>
                    reportGenerator.GenerateReportAsync(folder, report, ct),
                generateChart: (folder, report, log) =>
                    ChartGenerator.GenerateChartAsync(folder, report, log, ct),
                logger),

            StopMonitoring: () => host.StopAsync(CancellationToken.None),
            DisconnectEventPublisher: () => eventPublisher.DisconnectAsync(CancellationToken.None));
    }
}
