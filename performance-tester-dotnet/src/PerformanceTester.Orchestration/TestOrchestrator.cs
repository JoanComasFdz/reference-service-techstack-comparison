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
            // Phase 0: Setup
            progress?.Report(PhaseInfo.Starting(TestPhase.Setup, "Starting service discovery and infrastructure setup"));
            var serviceProcessId = await SetupPhase.ExecuteAsync(
                configuration, testRunId,
                _serviceDiscovery, _host, _hostLifetime, _dockerMonitors,
                _database, _rabbitMq, _eventPublisher,
                _logger, cancellationToken);
            progress?.Report(PhaseInfo.Completed(TestPhase.Setup, $"Setup complete, service PID: {serviceProcessId}"));

            // Phase 0.5: Warmup (failures abort the test)
            currentPhase = TestPhase.Warmup;
            progress?.Report(PhaseInfo.Starting(TestPhase.Warmup, $"Starting warmup with {configuration.WarmupEventCount} events"));
            var warmupStartTime = DateTime.UtcNow;
            await WarmupPhase.ExecuteAsync(
                configuration,
                _eventPublisher, _eventConsumer, _database, _rabbitMq,
                _logger, cancellationToken);
            var warmupEndTime = DateTime.UtcNow;
            progress?.Report(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            currentPhase = TestPhase.EventTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var (publishMetrics, eventTestStartTime, eventTestEndTime) =
                await EventTestPhase.ExecuteAsync(
                    configuration, serviceProcessId,
                    _metricsCollector, _processMonitor, _systemMonitor, _dockerMonitors,
                    _eventConsumer, _eventPublisher,
                    progress, _logger, cancellationToken);
            progress?.Report(PhaseInfo.Completed(TestPhase.EventTest, $"Event test complete: {publishMetrics.EventsPerSecond:F2} events/s"));

            // Phase 2: API Load Test
            currentPhase = TestPhase.ApiTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"Starting API test for {configuration.ApiDuration.Value.TotalSeconds}s"));
            var (apiResult, apiTestStartTime, apiTestEndTime) =
                await ApiTestPhase.ExecuteAsync(
                    configuration,
                    _apiLoadTester,
                    progress, _logger, cancellationToken);
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
