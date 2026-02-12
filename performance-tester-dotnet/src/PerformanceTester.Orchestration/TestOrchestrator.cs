using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.SystemMonitoring;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Orchestrates complete performance test workflow from setup through reporting.
/// Delegates phase execution to dedicated static classes.
/// </summary>
public class TestOrchestrator(
    IHost host,
    IHostApplicationLifetime hostLifetime,
    ILogger<TestOrchestrator> logger,
    IServiceDiscovery serviceDiscovery,
    IDatabase database,
    IRabbitMQ rabbitMq,
    IEventPublisher eventPublisher,
    IEventConsumer eventConsumer,
    IMetricsCollector metricsCollector,
    IProcessMonitor processMonitor,
    ISystemMonitor systemMonitor,
    IEnumerable<IDockerMonitor> dockerMonitors,
    IApiLoadTester apiLoadTester,
    ISystemInfoDetector systemInfoDetector,
    ReportGenerator reportGenerator) : ITestOrchestrator
{
    /// <inheritdoc />
    public async Task<TestReport> RunTestAsync(
        TestConfiguration configuration,
        IProgress<PhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var testRunId = Guid.NewGuid();
        var testStartTime = DateTime.UtcNow;

        using var logContextTestRunId = LogContext.PushProperty("TestRunId", testRunId);

        logger.LogInformation(
            "Starting performance test run {TestRunId} for service on port {Port}",
            testRunId,
            configuration.ServicePort);

        // Track current phase for accurate failure reporting
        var currentPhase = TestPhase.Setup;

        try
        {
            // Shared delegates for multiple phases
            PhasesToolbox.ClearDatabase clearDatabase = () => database.ClearDatabaseAsync(configuration.DatabaseName.Value, cancellationToken);
            PhasesToolbox.ClearAllQueues clearAllQueues = async () =>
            {
                var result = await rabbitMq.ClearAllQueuesAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                return result;
            };
            PhasesToolbox.PublishEvents publishEvents = (count) => eventPublisher.PublishEventsAsync(count, cancellationToken);
            PhasesToolbox.TrackEvents trackEvents = (count, timeout, progress) => eventConsumer.StartTrackingEventsAsync(count, timeout, progress, cancellationToken);
            Task forAllDockerMonitors(Func<IDockerMonitor, Task> action) => Task.WhenAll(dockerMonitors.Select(action));

            // Phase 0: Setup
            progress?.Report(PhaseInfo.Starting(TestPhase.Setup, "Starting service discovery and infrastructure setup"));
            var setupResult = await SetupPhase.ExecuteAsync(
                testRunId,
                findServiceProcessId: () => serviceDiscovery.FindServiceProcessIdAsync(configuration.ServicePort.Value, TimeSpan.FromSeconds(30), cancellationToken),
                isMonitoringStarted: () => hostLifetime.ApplicationStarted.IsCancellationRequested,
                startMonitoring: () => host.StartAsync(cancellationToken),
                warmupDockerApi: () => forAllDockerMonitors(m => m.WarmupAsync(cancellationToken)),
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                connectEventPublisher: () => eventPublisher.ConnectAsync(cancellationToken), logger);

            var serviceProcessId = setupResult.Match(
                success: s => s.Value,
                failure: f => throw new InvalidOperationException(f.Error));
            progress?.Report(PhaseInfo.Completed(TestPhase.Setup, $"Setup complete, service PID: {serviceProcessId}"));

            // Phase 0.5: Warmup (failures abort the test)
            currentPhase = TestPhase.Warmup;
            progress?.Report(PhaseInfo.Starting(TestPhase.Warmup, $"Starting warmup with {configuration.WarmupEventCount} events"));
            var warmupStartTime = DateTime.UtcNow;
            await WarmupPhase.ExecuteAsync(
                configuration,
                trackEvents: trackEvents,
                publishEvents: publishEvents,
                executeWarmupApiCalls: (url, count) => WarmupPhase.ExecuteWarmupApiCallsAsync(url, count, logger, cancellationToken),
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                logger);
            var warmupEndTime = DateTime.UtcNow;
            progress?.Report(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            currentPhase = TestPhase.EventTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var (publishMetrics, eventTestStartTime, eventTestEndTime) = await EventTestPhase.ExecuteAsync(
                    configuration, serviceProcessId,
                    systemCpuCount: systemMonitor.CpuCount, 
                    systemIsWsl2: systemMonitor.IsWsl2,
                    clearSamples: metricsCollector.ClearSamples,
                    startProcessMonitoring: (pid) => processMonitor.StartMonitoringAsync(pid, cancellationToken: cancellationToken),
                    startSystemMonitoring: () => systemMonitor.StartMonitoringAsync(cancellationToken: cancellationToken),
                    startDockerMonitoring: () => forAllDockerMonitors(m => m.StartMonitoringAsync(cancellationToken: cancellationToken)),
                    trackEvents: trackEvents,
                    publishEvents: publishEvents,
                    progress, logger);
            progress?.Report(PhaseInfo.Completed(TestPhase.EventTest, $"Event test complete: {publishMetrics.EventsPerSecond:F2} events/s"));

            // Phase 2: API Load Test
            currentPhase = TestPhase.ApiTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"Starting API test for {configuration.ApiDuration.Value.TotalSeconds}s"));
            var (apiResult, apiTestStartTime, apiTestEndTime) = await ApiTestPhase.ExecuteAsync(
                    configuration,
                    (url, duration, vus, apiProgress, maxFail, dir) => apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, cancellationToken),
                    progress,
                    logger);
            // Report phase completion - Failed if aborted due to consecutive errors, Completed otherwise
            var apiPhaseResult = apiResult.WasAborted
                ? PhaseInfo.Failed(TestPhase.ApiTest, apiResult.AbortReason ?? "API test aborted")
                : PhaseInfo.Completed(TestPhase.ApiTest, $"API test complete: {apiResult.RequestsPerSecond:F2} req/s");
            progress?.Report(apiPhaseResult);

            var testEndTime = DateTime.UtcNow;

            // Build internal result
            var testResult = new TestResult
            {
                TestRunId = testRunId,
                TestStartTime = testStartTime,
                TestEndTime = testEndTime,
                Configuration = configuration,
                ServiceProcessId = serviceProcessId,
                ServiceProcessName = "service", // Will be updated from metrics
                WarmupStartTime = warmupStartTime,
                WarmupEndTime = warmupEndTime,
                EventTestStartTime = eventTestStartTime,
                EventTestEndTime = eventTestEndTime,
                ApiTestStartTime = apiTestStartTime,
                ApiTestEndTime = apiTestEndTime,
                PublishMetrics = publishMetrics,
                ApiLoadTestResult = apiResult,
                ThroughputSamples = Array.Empty<EventThroughputSample>(), // Collected in Phase 3
                ProcessMetrics = Array.Empty<ProcessMetrics>(),
                RabbitMqMetrics = Array.Empty<DockerMetrics>(),
                PostgresMetrics = Array.Empty<DockerMetrics>(),
                SystemInfo = null
            };

            // Phase 3: Reporting (stops monitors, collects metrics, generates reports)
            currentPhase = TestPhase.Reporting;
            progress?.Report(PhaseInfo.Starting(TestPhase.Reporting, "Starting metrics collection and report generation"));
            var testReport = await ReportingPhase.ExecuteAsync(
                testResult, configuration,
                eventPublisher, host, metricsCollector, processMonitor, systemMonitor,
                dockerMonitors, systemInfoDetector, reportGenerator,
                logger, cancellationToken);
            progress?.Report(PhaseInfo.Completed(TestPhase.Reporting, "Report generation complete"));

            logger.LogInformation(
                "Performance test run {TestRunId} completed successfully in {Duration:F2}s",
                testRunId,
                (testEndTime - testStartTime).TotalSeconds);

            return testReport;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Performance test run {TestRunId} failed: {Message}",
                testRunId,
                ex.Message);

            // Report failure with the actual phase that failed
            progress?.Report(PhaseInfo.Failed(currentPhase, ex.Message));

            // Ensure monitoring services are stopped
            try
            {
                // Try to disconnect event publisher first
                try
                {
                    await eventPublisher.DisconnectAsync(CancellationToken.None);
                }
                catch (Exception disconnectEx)
                {
                    logger.LogWarning(
                        disconnectEx,
                        "Failed to disconnect event publisher during error cleanup");
                }

                await host.StopAsync(CancellationToken.None);
            }
            catch (Exception stopEx)
            {
                logger.LogWarning(
                    stopEx,
                    "Failed to stop monitoring services during error cleanup");
            }

            throw;
        }
    }
}
