using JoanComasFdz.Result;
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
    public async Task<Result<TestReport, TestRunFailure>> RunTestAsync(
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
        IReadOnlyCollection<DockerMetrics> dockerMetricsFor(NonEmptyString containerName) => dockerMonitors.Single(m => m.ContainerName == containerName.Value).GetCollectedMetrics();

        TestRunFailure Fail(TestPhase phase, string message)
        {
            logger.LogError("Performance test run {TestRunId} failed in {Phase}: {Message}", testRunId, phase, message);
            progress?.Report(PhaseInfo.Failed(phase, message));
            return new TestRunFailure(phase, message);
        }

        try
        {
            // Phase 0: Setup (before try — nothing to clean up yet)
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

            if (setupResult.IsFailure)
            {
                return Fail(TestPhase.Setup, setupResult.FailureError);
            }

            var serviceProcessId = setupResult.SuccessValue;
            progress?.Report(PhaseInfo.Completed(TestPhase.Setup, $"Setup complete, service PID: {serviceProcessId}"));

            // Phase 0.5: Warmup (failures abort the test)
            progress?.Report(PhaseInfo.Starting(TestPhase.Warmup, $"Starting warmup with {configuration.WarmupEventCount} events"));
            var warmupStartTime = DateTime.UtcNow;
            var warmupResult = await WarmupPhase.ExecuteAsync(
                configuration,
                trackEvents: trackEvents,
                publishEvents: publishEvents,
                executeWarmupApiCalls: (url, count) => WarmupPhase.ExecuteWarmupApiCallsAsync(url, count, logger, cancellationToken),
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                logger);
            if (warmupResult.IsFailure)
            {
                return Fail(TestPhase.Warmup, warmupResult.FailureError);
            }

            var warmupEndTime = DateTime.UtcNow;
            progress?.Report(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            progress?.Report(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var eventTestResult = await EventTestPhase.ExecuteAsync(
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
            if (eventTestResult.IsFailure)
            {
                return Fail(TestPhase.EventTest, eventTestResult.FailureError);
            }

            var eventTestOutput = eventTestResult.SuccessValue;
            var publishMetrics = eventTestOutput.PublishMetrics;
            var eventTestStartTime = eventTestOutput.StartTime;
            var eventTestEndTime = eventTestOutput.EndTime;
            progress?.Report(PhaseInfo.Completed(TestPhase.EventTest, $"Event test complete: {publishMetrics.EventsPerSecond:F2} events/s"));

            // Phase 2: API Load Test
            progress?.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"Starting API test for {configuration.ApiDuration.Value.TotalSeconds}s"));
            var apiTestResult = await ApiTestPhase.ExecuteAsync(
                configuration,
                (url, duration, vus, apiProgress, maxFail, dir) => apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, cancellationToken),
                progress,
                logger);
            if (apiTestResult.IsFailure)
            {
                return Fail(TestPhase.ApiTest, apiTestResult.FailureError);
            }

            var apiTestOutput = apiTestResult.SuccessValue;
            var apiResult = apiTestOutput.ApiLoadTestResult;
            var apiTestStartTime = apiTestOutput.StartTime;
            var apiTestEndTime = apiTestOutput.EndTime;
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

            // Teardown
            logger.LogInformation("Disconnecting from RabbitMQ event publisher...");
            await eventPublisher.DisconnectAsync(cancellationToken);
            logger.LogInformation("RabbitMQ event publisher disconnected");

            logger.LogInformation("Stopping monitoring services...");
            await host.StopAsync(cancellationToken);
            logger.LogInformation("All monitoring services stopped");

            // Phase 3: Reporting (collects metrics, generates reports)
            progress?.Report(PhaseInfo.Starting(TestPhase.Reporting, "Starting metrics collection and report generation"));
            var reportingResult = await ReportingPhase.ExecuteAsync(
                testResult, configuration,
                getThroughputSamples: metricsCollector.GetThroughputSamples,
                getProcessMetrics: processMonitor.GetCollectedMetrics,
                getSystemMetrics: systemMonitor.GetCollectedMetrics,
                getRabbitMqMetrics: () => dockerMetricsFor(configuration.RabbitMqContainerName),
                getPostgresMetrics: () => dockerMetricsFor(configuration.PostgresContainerName),
                getSystemInfo: () => systemInfoDetector.GetSystemInfoAsync(cancellationToken),
                generateReport: (folder, report) => reportGenerator.GenerateReportAsync(folder, report, cancellationToken),
                generateChart: (folder, report, log) => ChartGenerator.GenerateChartAsync(folder, report, log, cancellationToken),
                logger);
            if (reportingResult.IsFailure)
            {
                return Fail(TestPhase.Reporting, reportingResult.FailureError);
            }

            var testReport = reportingResult.SuccessValue;
            progress?.Report(PhaseInfo.Completed(TestPhase.Reporting, "Report generation complete"));

            logger.LogInformation(
                "Performance test run {TestRunId} completed successfully in {Duration:F2}s",
                testRunId,
                (testEndTime - testStartTime).TotalSeconds);

            return testReport;
        }
        finally
        {
            // Best-effort cleanup (disconnect publisher, stop host)
            try { await eventPublisher.DisconnectAsync(CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to disconnect event publisher during cleanup"); }

            try { await host.StopAsync(CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to stop monitoring services during cleanup"); }
        }
    }
}
