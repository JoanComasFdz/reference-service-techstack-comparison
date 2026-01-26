using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Orchestrates complete performance test workflow from setup through reporting.
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
    private readonly IEnumerable<IDockerMonitor> _dockerMonitors;
    private readonly IApiLoadTester _apiLoadTester;

    // Phase 3: Reporting
    private readonly ISystemInfoDetector _systemInfoDetector;
    private readonly IReportGenerator _reportGenerator;
    private readonly IChartGenerator _chartGenerator;

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
        IEnumerable<IDockerMonitor> dockerMonitors,
        IApiLoadTester apiLoadTester,
        ISystemInfoDetector systemInfoDetector,
        IReportGenerator reportGenerator,
        IChartGenerator chartGenerator)
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
        _dockerMonitors = dockerMonitors ?? throw new ArgumentNullException(nameof(dockerMonitors));
        _apiLoadTester = apiLoadTester ?? throw new ArgumentNullException(nameof(apiLoadTester));
        _systemInfoDetector = systemInfoDetector ?? throw new ArgumentNullException(nameof(systemInfoDetector));
        _reportGenerator = reportGenerator ?? throw new ArgumentNullException(nameof(reportGenerator));
        _chartGenerator = chartGenerator ?? throw new ArgumentNullException(nameof(chartGenerator));
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
            var serviceProcessId = await ExecuteSetupPhaseAsync(configuration, testRunId, cancellationToken);
            progress?.Report(PhaseInfo.Completed(TestPhase.Setup, $"Setup complete, service PID: {serviceProcessId}"));

            // Phase 0.5: Warmup (failures abort the test)
            currentPhase = TestPhase.Warmup;
            progress?.Report(PhaseInfo.Starting(TestPhase.Warmup, $"Starting warmup with {configuration.WarmupEventCount} events"));
            var warmupStartTime = DateTime.UtcNow;
            await ExecuteWarmupPhaseAsync(configuration, cancellationToken);
            var warmupEndTime = DateTime.UtcNow;
            progress?.Report(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            currentPhase = TestPhase.EventTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var (publishMetrics, eventTestStartTime, eventTestEndTime) =
                await ExecuteEventTestPhaseAsync(configuration, cancellationToken);
            progress?.Report(PhaseInfo.Completed(TestPhase.EventTest, $"Event test complete: {publishMetrics.EventsPerSecond:F2} events/s"));

            // Phase 2: API Load Test
            currentPhase = TestPhase.ApiTest;
            progress?.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"Starting API test for {configuration.ApiDurationOrDefault.TotalSeconds}s"));
            var (apiResult, apiTestStartTime, apiTestEndTime) =
                await ExecuteApiTestPhaseAsync(configuration, cancellationToken);
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
            var testReport = await ExecuteReportingPhaseAsync(
                testResult,
                configuration,
                cancellationToken);
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

    private async Task<int> ExecuteSetupPhaseAsync(
        TestConfiguration config,
        Guid testRunId,
        CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("TestRunId", testRunId);
        using var __ = LogContext.PushProperty("Phase", "Setup");

        _logger.LogInformation(
            "Starting setup phase for service on port {Port} (database: {Database})",
            config.ServicePort,
            config.DatabaseName);

        // Step 1: Find service process
        _logger.LogInformation("Discovering service on port {Port}...", config.ServicePort);

        var serviceProcessId = await _serviceDiscovery.FindServiceProcessIdAsync(
            config.ServicePort,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken);

        if (serviceProcessId is null)
        {
            throw new TimeoutException(
                $"Service not found on port {config.ServicePort} within 30 seconds. " +
                "Ensure the service is running and listening on the specified port.");
        }

        _logger.LogInformation(
            "Service discovered: PID {ProcessId}",
            serviceProcessId.Value);

        // Step 2: Start IHost (all BackgroundServices start, ProcessMonitor waits)
        // Check if host is already started (e.g., by System.CommandLine.Hosting in CLI)
        // IHostApplicationLifetime.ApplicationStarted is cancelled when the host has started
        if (_hostLifetime.ApplicationStarted.IsCancellationRequested)
        {
            _logger.LogInformation("Host already started, monitoring services are running");
        }
        else
        {
            _logger.LogInformation("Starting monitoring services...");
            await _host.StartAsync(cancellationToken);
            _logger.LogInformation("All monitoring services started");
        }

        // Step 3: Start process monitoring (deferred start pattern)
        await _processMonitor.StartMonitoringAsync(serviceProcessId.Value, cancellationToken: cancellationToken);
        _logger.LogInformation("Process monitoring started for PID {ProcessId}", serviceProcessId.Value);

        // Step 3.5: Warm up Docker API and start monitoring
        // Docker monitoring starts early (like Python) to capture the entire test lifecycle
        // First call is slow (~2-3 seconds), so warmup before starting monitors
        // Run in parallel for all monitors
        var dockerMonitorsList = _dockerMonitors.ToList();
        _logger.LogInformation("Warming up Docker API for {Count} monitors: {Names}...",
            dockerMonitorsList.Count,
            string.Join(", ", dockerMonitorsList.Select(m => m.ContainerName)));
        var warmupTasks = dockerMonitorsList.Select(m => m.WarmupAsync(cancellationToken));
        await Task.WhenAll(warmupTasks);
        _logger.LogInformation("Docker API warmup complete");

        // Step 3.6: Start Docker container monitoring (before clearing DB/queues)
        // This matches Python's approach: monitoring starts early and captures the entire test
        _logger.LogInformation("Starting Docker container monitors...");
        var dockerStartTasks = dockerMonitorsList.Select(m => m.StartMonitoringAsync(cancellationToken: cancellationToken));
        await Task.WhenAll(dockerStartTasks);
        _logger.LogInformation("Docker container monitors started (first samples collected)");

        // Step 4: Clear database
        _logger.LogInformation("Clearing database {Database}...", config.DatabaseName);
        await _database.ClearDatabaseAsync(config.DatabaseName, cancellationToken);
        _logger.LogInformation("Database cleared");

        // Step 5: Clear RabbitMQ queues
        _logger.LogInformation("Clearing RabbitMQ queues...");
        await _rabbitMq.ClearAllQueuesAsync(cancellationToken);
        _logger.LogInformation("RabbitMQ queues cleared");

        // Allow time for RabbitMQ consumers to recover after queue purge
        // Queue purging can temporarily disrupt active consumers, this delay ensures they're ready
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        // Step 6: Connect to RabbitMQ event publisher
        _logger.LogInformation("Connecting to RabbitMQ event publisher...");
        await _eventPublisher.ConnectAsync(cancellationToken);
        _logger.LogInformation("RabbitMQ event publisher connected");

        _logger.LogInformation("Setup phase complete");

        return serviceProcessId.Value;
    }

    private async Task ExecuteWarmupPhaseAsync(
        TestConfiguration config,
        CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("Phase", "Warmup");

        _logger.LogInformation("Starting warmup phase (not measured)");

        try
        {

            _logger.LogInformation(
                "Warmup: Publishing and consuming {Count} events (timeout: {Timeout}s)",
                config.WarmupEventCount,
                config.WarmupInactivityTimeoutOrDefault.TotalSeconds);

            // Start consumer tracking
            var consumerTask = _eventConsumer.StartTrackingEventsAsync(
                config.WarmupEventCount,
                config.WarmupInactivityTimeoutOrDefault,
                progress: null,
                cancellationToken);

            // Publish warmup events
            var publishMetrics = await _eventPublisher.PublishEventsAsync(
                config.WarmupEventCount,
                cancellationToken);

            _logger.LogInformation(
                "Warmup: Published {Count} events in {Duration:F2}s ({Rate:F2} events/s)",
                publishMetrics.EventCount,
                publishMetrics.Duration.TotalSeconds,
                publishMetrics.EventsPerSecond);

            // Wait for consumption
            await consumerTask;

            _logger.LogInformation("Warmup: All {Count} events consumed", config.WarmupEventCount);

            // Warmup API using simple HttpClient (not k6)
            _logger.LogInformation(
                "Warmup: Making {Count} HTTP calls to API endpoint",
                config.WarmupApiCallCount);

            using var httpClient = new HttpClient();
            var successCount = 0;
            var failCount = 0;

            for (var i = 0; i < config.WarmupApiCallCount; i++)
            {
                try
                {
                    var response = await httpClient.GetAsync(config.ApiUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning(
                            "Warmup API call {Index} failed with status {StatusCode}",
                            i + 1,
                            response.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogWarning(
                        ex,
                        "Warmup API call {Index} failed with exception",
                        i + 1);
                }
            }

            _logger.LogInformation(
                "Warmup: API calls complete - {Success} succeeded, {Failed} failed",
                successCount,
                failCount);

            // Clear database and queues again
            _logger.LogInformation("Warmup: Clearing database and queues before measured test");
            await _database.ClearDatabaseAsync(config.DatabaseName, cancellationToken);
            await _rabbitMq.ClearAllQueuesAsync(cancellationToken);

            _logger.LogInformation("Warmup phase complete - starting measured test");
        }
        catch (Exception ex)
        {
            // Warmup failures should abort the test
            _logger.LogError(
                ex,
                "Warmup phase failed, aborting test. " +
                "Fix the warmup configuration or service availability before running the measured test.");
            throw;
        }
    }

    private async Task<(PublishMetrics PublishMetrics, DateTime StartTime, DateTime EndTime)>
        ExecuteEventTestPhaseAsync(
            TestConfiguration config,
            CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("Phase", "EventTest");

        _logger.LogInformation(
            "Starting event throughput test with {Count} events",
            config.EventCount);

        // Docker monitors already started in Setup phase (like Python)
        // This ensures monitoring captures the entire test lifecycle

        var startTime = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // CRITICAL: Start publisher and consumer CONCURRENTLY (not sequentially!)
        var consumerTask = _eventConsumer.StartTrackingEventsAsync(
            config.EventCount,
            config.InactivityTimeoutOrDefault,
            progress: null,
            cancellationToken);

        var publisherTask = _eventPublisher.PublishEventsAsync(
            config.EventCount,
            cancellationToken);

        // Wait for both to complete
        await Task.WhenAll(consumerTask, publisherTask);
        var publishMetrics = await publisherTask; // Get result from publisher task

        stopwatch.Stop();
        var endTime = DateTime.UtcNow;

        var totalDuration = stopwatch.Elapsed;
        var eventThroughput = config.EventCount / totalDuration.TotalSeconds;

        _logger.LogInformation(
            "Event throughput test complete: {Count} events in {Duration:F2}s ({Rate:F2} events/s)",
            config.EventCount,
            totalDuration.TotalSeconds,
            eventThroughput);

        _logger.LogInformation(
            "Publishing: {Count} events in {Duration:F2}s ({Rate:F2} events/s)",
            publishMetrics.EventCount,
            publishMetrics.Duration.TotalSeconds,
            publishMetrics.EventsPerSecond);

        return (publishMetrics, startTime, endTime);
    }

    private async Task<(ApiLoadTestResult Result, DateTime StartTime, DateTime EndTime)>
        ExecuteApiTestPhaseAsync(
            TestConfiguration config,
            CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("Phase", "ApiTest");

        _logger.LogInformation(
            "Starting API load test for {Duration}s with {Workers} worker(s)",
            config.ApiDurationOrDefault.TotalSeconds,
            config.ApiWorkers);

        var startTime = DateTime.UtcNow;

        var result = await _apiLoadTester.StartTestAsync(
            config.ApiUrl,
            config.ApiDurationOrDefault,
            config.ApiWorkers,
            config.MaxConsecutiveApiFailures,
            cancellationToken);

        var endTime = DateTime.UtcNow;

        _logger.LogInformation(
            "API load test complete: {Requests} requests in {Duration:F2}s " +
            "({Rate:F2} req/s, {Failures} failures)",
            result.TotalRequests,
            result.TotalDuration.TotalSeconds,
            result.RequestsPerSecond,
            result.FailedRequests);

        _logger.LogInformation(
            "API response times: P95={P95:F2}ms, P99={P99:F2}ms",
            result.P95RequestDurationMs,
            result.P99RequestDurationMs);

        return (result, startTime, endTime);
    }

    private async Task<TestReport> ExecuteReportingPhaseAsync(
        TestResult testResult,
        TestConfiguration config,
        CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("Phase", "Reporting");

        _logger.LogInformation("Starting reporting phase");

        // Step 1: Disconnect from RabbitMQ event publisher
        _logger.LogInformation("Disconnecting from RabbitMQ event publisher...");
        await _eventPublisher.DisconnectAsync(cancellationToken);
        _logger.LogInformation("RabbitMQ event publisher disconnected");

        // Step 2: Stop IHost (all BackgroundServices stop)
        _logger.LogInformation("Stopping monitoring services...");
        await _host.StopAsync(cancellationToken);
        _logger.LogInformation("All monitoring services stopped");

        // Step 3: Collect all metrics from monitors
        _logger.LogInformation("Collecting metrics from monitors...");

        var throughputSamples = _metricsCollector.GetThroughputSamples();
        var processMetrics = _processMonitor.GetCollectedMetrics();

        // Get Docker monitors by container name
        var rabbitMqMonitor = _dockerMonitors.Single(m => m.ContainerName == config.RabbitMqContainerName);
        var postgresMonitor = _dockerMonitors.Single(m => m.ContainerName == config.PostgresContainerName);

        var rabbitMqMetrics = rabbitMqMonitor?.GetCollectedMetrics() ?? [];
        var postgresMetrics = postgresMonitor?.GetCollectedMetrics() ?? [];

        _logger.LogInformation(
            "Metrics collected: {Throughput} throughput samples, " +
            "{Process} process samples, {RabbitMQ} RabbitMQ samples, " +
            "{Postgres} PostgreSQL samples",
            throughputSamples.Count,
            processMetrics.Count,
            rabbitMqMetrics.Count,
            postgresMetrics.Count);

        // Step 4: Get system information (cached)
        var systemInfo = await _systemInfoDetector.GetSystemInfoAsync(cancellationToken);

        // Step 5: Build TestReport
        _logger.LogInformation("Building test report...");

        var testReport = BuildTestReport(
            testResult,
            config,
            throughputSamples,
            processMetrics,
            rabbitMqMetrics,
            postgresMetrics,
            systemInfo);

        // Step 6: Generate JSON reports
        _logger.LogInformation("Generating JSON reports to {Folder}", config.ResultsFolder);

        Directory.CreateDirectory(config.ResultsFolder);

        await _reportGenerator.GenerateReportAsync(
            config.ResultsFolder,
            testReport,
            cancellationToken);

        _logger.LogInformation("JSON reports generated");

        // Step 7: Generate chart
        var serviceName = processMetrics.FirstOrDefault()?.ProcessName ?? "unknown";
        var chartPath = Path.Combine(
            config.ResultsFolder,
            $"test-report-{testReport.TestDate:yyyyMMdd_HHmmss}-{serviceName.ToLowerInvariant()}.chart.png");

        _logger.LogInformation("Generating chart to {Path}", chartPath);

        await _chartGenerator.GenerateChartAsync(
            chartPath,
            testReport,
            cancellationToken);

        _logger.LogInformation("Chart generated");

        _logger.LogInformation("Reporting phase complete");

        return testReport;
    }

    private static TestReport BuildTestReport(
        TestResult testResult,
        TestConfiguration config,
        IReadOnlyCollection<EventThroughputSample> throughputSamples,
        IReadOnlyCollection<ProcessMetrics> processMetrics,
        IReadOnlyCollection<DockerMetrics> rabbitMqMetrics,
        IReadOnlyCollection<DockerMetrics> postgresMetrics,
        SystemInfo? systemInfo)
    {
        // Calculate phase durations
        var warmupDuration = testResult.WarmupEndTime - testResult.WarmupStartTime;
        var eventTestDuration = testResult.EventTestEndTime - testResult.EventTestStartTime;
        var apiTestDuration = testResult.ApiTestEndTime - testResult.ApiTestStartTime;
        var totalDuration = testResult.TestEndTime - testResult.TestStartTime;

        // Get process name from first sample
        var serviceName = processMetrics.FirstOrDefault()?.ProcessName ?? "unknown";

        // Calculate elapsed times from test start
        var testStartTime = testResult.TestStartTime;
        var eventTestElapsedStart = (testResult.EventTestStartTime - testStartTime).TotalSeconds;
        var phase1Start = eventTestElapsedStart;  // Publisher starts when event test starts
        var phase1End = eventTestElapsedStart + testResult.PublishMetrics.Duration.TotalSeconds;  // Publisher completes after its duration
        var phase2Start = eventTestElapsedStart;  // Consumer starts concurrently with publisher
        var phase2End = (testResult.EventTestEndTime - testStartTime).TotalSeconds;
        var phase3Start = (testResult.ApiTestStartTime - testStartTime).TotalSeconds;
        var phase3End = (testResult.ApiTestEndTime - testStartTime).TotalSeconds;

        // Convert metrics to report sample formats
        var processResourceSamples = processMetrics.Select(m => new ProcessResourceSample
        {
            Timestamp = m.Timestamp.DateTime,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryRssMb = m.MemoryMB,
            Threads = m.ThreadCount
        }).ToList();

        var rabbitMqResourceSamples = rabbitMqMetrics.Select(m => new ContainerResourceSample
        {
            Timestamp = m.Timestamp,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryMb = m.MemoryMB
        }).ToList();

        var postgresResourceSamples = postgresMetrics.Select(m => new ContainerResourceSample
        {
            Timestamp = m.Timestamp,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryMb = m.MemoryMB
        }).ToList();

        // Convert event throughput samples
        var eventThroughputSamples = throughputSamples.Select(s => new Reporting.ThroughputMetricSample
        {
            Timestamp = s.Timestamp.DateTime,
            ElapsedSeconds = (s.Timestamp - testResult.TestStartTime).TotalSeconds,
            Rate = s.ThroughputEventsPerSecond,
            CumulativeCount = s.CumulativeEventCount
        }).ToList();

        // Convert API throughput samples
        var apiThroughputSamples = testResult.ApiLoadTestResult.ThroughputSamples.Select(s => new Reporting.ThroughputMetricSample
        {
            Timestamp = s.Timestamp.DateTime,
            ElapsedSeconds = (s.Timestamp - testResult.TestStartTime).TotalSeconds,
            Rate = s.RequestsPerSecond,
            CumulativeCount = s.CumulativeRequestCount
        }).ToList();

        return new TestReport
        {
            TestDate = testResult.TestStartTime,
            TotalRuntimeSeconds = totalDuration.TotalSeconds,
            PhaseTimestamps = new PhaseTimestamps
            {
                Phase1Start = phase1Start,
                Phase1End = phase1End,
                Phase2Start = phase2Start,
                Phase2End = phase2End,
                Phase3Start = phase3Start,
                Phase3End = phase3End
            },
            MonitoredProcess = new MonitoredProcess
            {
                Name = serviceName,
                Pid = testResult.ServiceProcessId,
                Port = config.ServicePort
            },
            System = systemInfo ?? throw new InvalidOperationException("System info is required"),
            Configuration = new Reporting.TestConfiguration
            {
                NumEvents = config.EventCount,
                ApiDuration = FormatDuration(config.ApiDurationOrDefault),
                ApiConcurrentWorkers = config.ApiWorkers,
                RabbitmqExchange = "referenceservice.comparison",
                ConsumerQueue = "instrument-status-changed",
                ApiEndpoint = config.ApiUrl,
                PublishEventType = "com.referenceservice.instrument.status.changed",
                ConsumeEventType = "com.referenceservice.instrumentstatus.kpi.updated"
            },
            Results = new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = testResult.PublishMetrics.Duration.TotalSeconds,
                    ThroughputEventsPerSec = testResult.PublishMetrics.EventsPerSecond
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = eventTestDuration.TotalSeconds,
                    ThroughputEventsPerSec = config.EventCount / eventTestDuration.TotalSeconds
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = testResult.ApiLoadTestResult.TotalDuration.TotalSeconds,
                    TotalRequests = testResult.ApiLoadTestResult.TotalRequests,
                    ThroughputCallsPerSec = testResult.ApiLoadTestResult.RequestsPerSecond,
                    SuccessCount = testResult.ApiLoadTestResult.TotalRequests - testResult.ApiLoadTestResult.FailedRequests,
                    SuccessPercentage = ((testResult.ApiLoadTestResult.TotalRequests - testResult.ApiLoadTestResult.FailedRequests) / (double)testResult.ApiLoadTestResult.TotalRequests) * 100,
                    ErrorCount = testResult.ApiLoadTestResult.FailedRequests,
                    ErrorPercentage = (testResult.ApiLoadTestResult.FailedRequests / (double)testResult.ApiLoadTestResult.TotalRequests) * 100,
                    WasAborted = testResult.ApiLoadTestResult.WasAborted,
                    AbortReason = testResult.ApiLoadTestResult.AbortReason
                }
            },
            EventsThroughputSamples = eventThroughputSamples,
            ApiThroughputSamples = apiThroughputSamples,
            ProcessResourceSamples = processResourceSamples,
            SystemResourceSamples = Array.Empty<SystemResourceSample>(),
            RabbitMqResourceSamples = rabbitMqResourceSamples,
            PostgresResourceSamples = postgresResourceSamples
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h";
        }
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }
        return $"{(int)duration.TotalSeconds}s";
    }
}
