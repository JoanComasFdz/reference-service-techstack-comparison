using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.Infrastructure.Database;
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
public class TestOrchestrator : ITestOrchestrator
{
    private readonly IHost _host;
    private readonly IHostApplicationLifetime _hostLifetime;
    private readonly ILogger<TestOrchestrator> _logger;

    // Phase 1: Infrastructure
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly IDatabase _database;
    private readonly IRabbitMQ _rabbitMq;

    // Phase 2: Data Collection
    private readonly IEventPublisher _eventPublisher;
    private readonly IEventConsumer _eventConsumer;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IProcessMonitor _processMonitor;
    private readonly ISystemMonitor _systemMonitor;
    private readonly IEnumerable<IDockerMonitor> _dockerMonitors;
    private readonly IApiLoadTester _apiLoadTester;

    // Phase 3: Reporting
    private readonly ISystemInfoDetector _systemInfoDetector;
    private readonly ReportGenerator _reportGenerator;

    public TestOrchestrator(
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
        ReportGenerator reportGenerator)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _hostLifetime = hostLifetime ?? throw new ArgumentNullException(nameof(hostLifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _rabbitMq = rabbitMq ?? throw new ArgumentNullException(nameof(rabbitMq));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _processMonitor = processMonitor ?? throw new ArgumentNullException(nameof(processMonitor));
        _systemMonitor = systemMonitor ?? throw new ArgumentNullException(nameof(systemMonitor));
        _dockerMonitors = dockerMonitors ?? throw new ArgumentNullException(nameof(dockerMonitors));
        _apiLoadTester = apiLoadTester ?? throw new ArgumentNullException(nameof(apiLoadTester));
        _systemInfoDetector = systemInfoDetector ?? throw new ArgumentNullException(nameof(systemInfoDetector));
        _reportGenerator = reportGenerator ?? throw new ArgumentNullException(nameof(reportGenerator));
    }

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

        _logger.LogInformation(
            "Starting performance test run {TestRunId} for service on port {Port}",
            testRunId,
            configuration.ServicePort);

        // Track current phase for accurate failure reporting
        var currentPhase = TestPhase.Setup;

        try
        {
            // Shared delegates for multiple phases
            PhasesToolbox.ClearDatabase clearDatabase = () =>
                _database.ClearDatabaseAsync(configuration.DatabaseName.Value, cancellationToken);
            PhasesToolbox.ClearAllQueues clearAllQueues = async () =>
            {
                var result = await _rabbitMq.ClearAllQueuesAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                return result;
            };
            PhasesToolbox.PublishEvents publishEvents = (count) => _eventPublisher.PublishEventsAsync(count, cancellationToken);

            // Phase 0: Setup
            progress?.Report(PhaseInfo.Starting(TestPhase.Setup, "Starting service discovery and infrastructure setup"));
            var setupResult = await SetupPhase.ExecuteAsync(
                testRunId,
                findServiceProcessId: () => _serviceDiscovery.FindServiceProcessIdAsync(configuration.ServicePort.Value, TimeSpan.FromSeconds(30), cancellationToken),
                isMonitoringStarted: () => _hostLifetime.ApplicationStarted.IsCancellationRequested,
                startMonitoring: () => _host.StartAsync(cancellationToken),
                warmupDockerApi: async () =>
                {
                    var tasks = _dockerMonitors.Select(m => m.WarmupAsync(cancellationToken));
                    await Task.WhenAll(tasks);
                },
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                connectEventPublisher: () => _eventPublisher.ConnectAsync(cancellationToken), _logger);

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
                trackEvents: (count, timeout) => _eventConsumer.StartTrackingEventsAsync(count, timeout, progress: null, cancellationToken), // No progress reporting during warmup — it's a quick non-measured pre-heating step
                publishEvents: publishEvents,
                executeWarmupApiCalls: (url, count) => WarmupPhase.ExecuteWarmupApiCallsAsync(url, count, _logger, cancellationToken),
                clearDatabase: clearDatabase,
                clearAllQueues: clearAllQueues,
                _logger);
            var warmupEndTime = DateTime.UtcNow;
            progress?.Report(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            currentPhase = TestPhase.EventTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var (publishMetrics, eventTestStartTime, eventTestEndTime) =
                await EventTestPhase.ExecuteAsync(
                    configuration, serviceProcessId,
                    systemCpuCount: _systemMonitor.CpuCount,
                    systemIsWsl2: _systemMonitor.IsWsl2,
                    clearSamples: _metricsCollector.ClearSamples,
                    startProcessMonitoring: (pid) => _processMonitor.StartMonitoringAsync(pid, cancellationToken: cancellationToken),
                    startSystemMonitoring: () => _systemMonitor.StartMonitoringAsync(cancellationToken: cancellationToken),
                    startDockerMonitoring: async () =>
                    {
                        var tasks = _dockerMonitors.Select(m => m.StartMonitoringAsync(cancellationToken: cancellationToken));
                        await Task.WhenAll(tasks);
                    },
                    trackEvents: (count, timeout, consumerProgress) => _eventConsumer.StartTrackingEventsAsync(count, timeout, consumerProgress, cancellationToken),
                    publishEvents: publishEvents,
                    progress, _logger);
            progress?.Report(PhaseInfo.Completed(TestPhase.EventTest, $"Event test complete: {publishMetrics.EventsPerSecond:F2} events/s"));

            // Phase 2: API Load Test
            currentPhase = TestPhase.ApiTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"Starting API test for {configuration.ApiDuration.Value.TotalSeconds}s"));
            var (apiResult, apiTestStartTime, apiTestEndTime) = await ApiTestPhase.ExecuteAsync(
                    configuration,
                    (url, duration, vus, apiProgress, maxFail, dir) => _apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, cancellationToken),
                    progress,
                    _logger);
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
                _eventPublisher, _host, _metricsCollector, _processMonitor, _systemMonitor,
                _dockerMonitors, _systemInfoDetector, _reportGenerator,
                _logger, cancellationToken);
            progress?.Report(PhaseInfo.Completed(TestPhase.Reporting, "Report generation complete"));

            _logger.LogInformation(
                "Performance test run {TestRunId} completed successfully in {Duration:F2}s",
                testRunId,
                (testEndTime - testStartTime).TotalSeconds);

            return testReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(
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
                    await _eventPublisher.DisconnectAsync(CancellationToken.None);
                }
                catch (Exception disconnectEx)
                {
                    _logger.LogWarning(
                        disconnectEx,
                        "Failed to disconnect event publisher during error cleanup");
                }

                await _host.StopAsync(CancellationToken.None);
            }
            catch (Exception stopEx)
            {
                _logger.LogWarning(
                    stopEx,
                    "Failed to stop monitoring services during error cleanup");
            }

            throw;
        }
    }
}
