using PerformanceTester.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.Infrastructure.RabbitMQ;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Orchestration;
using Serilog.Context;

namespace PerformanceTester.Orchestration.Internal;

/// <summary>
/// Orchestrates complete performance test workflow from setup through reporting.
/// Defines phase-level delegates, bundles them into <see cref="Dependencies"/>,
/// builds them from DI via <see cref="BuildDependencies"/>,
/// and sequences execution in <see cref="RunTestAsync"/>.
/// </summary>
internal static class TestOrchestrationModule
{
    // -- Delegate definitions (what I need) ----------------------------------------

    /// <summary>
    /// Runs Setup phase: service discovery, infrastructure init, database/queue clearing.
    /// testRunId is generated at runtime by the orchestrator.
    /// </summary>
    public delegate Task<Result<ProcessId, string>> RunSetupDelegate(Guid testRunId);

    /// <summary>
    /// Runs Warmup phase: non-measured warmup events and API calls.
    /// All parameters (config, logger, CT, internal delegates) are pre-bound.
    /// </summary>
    public delegate Task<Result<Unit, string>> RunWarmupDelegate();

    /// <summary>
    /// Runs Event Test phase: concurrent publish/consume with monitoring.
    /// serviceProcessId comes from Setup output.
    /// </summary>
    public delegate Task<Result<EventTestPhaseModule.Output, string>> RunEventTestDelegate(ProcessId serviceProcessId);

    /// <summary>
    /// Runs API Load Test phase: k6 load test execution.
    /// </summary>
    public delegate Task<Result<ApiTestPhaseModule.Output, string>> RunApiTestDelegate();

    /// <summary>
    /// Runs Teardown phase: disconnect event publisher, stop monitoring services.
    /// Pre-bound with the active CancellationToken -- cancellable during normal flow.
    /// Must complete before reporting can collect metrics.
    /// </summary>
    public delegate Task<Result<Unit, string>> RunTeardownDelegate();

    /// <summary>
    /// Best-effort resource cleanup for the finally block.
    /// Pre-bound with CancellationToken.None -- must complete even after cancellation or failure.
    /// Runs the same operations as <see cref="RunTeardown"/> but is not cancellable.
    /// </summary>
    public delegate Task CleanupResourcesDelegate();

    /// <summary>
    /// Runs Reporting phase: metrics collection, report and chart generation.
    /// testResult is assembled at runtime from all phase outputs.
    /// </summary>
    public delegate Task<Result<TestReport, string>> RunReportingDelegate(TestResult testResult);

    // -- Dependencies record (bundle of what I need) -------------------------------

    /// <summary>
    /// Phase-level delegates for the test orchestrator.
    /// Each delegate has its internal plumbing (interfaces, config, CT) pre-bound
    /// by <see cref="BuildDependencies"/>.
    /// The orchestrator sequences these and threads inter-phase data.
    /// </summary>
    public record Dependencies(
        RunSetupDelegate RunSetup,
        RunWarmupDelegate RunWarmup,
        RunEventTestDelegate RunEventTest,
        RunApiTestDelegate RunApiTest,
        RunTeardownDelegate RunTeardown,
        RunReportingDelegate RunReporting,
        CleanupResourcesDelegate CleanupResources,
        ReportPhaseProgressDelegate ReportProgress
        );

    // -- Factory (how to build what I need from DI) --------------------------------

    /// <summary>
    /// Builds <see cref="Dependencies"/> by composing per-phase dependency classes.
    /// Resolves shared interfaces and creates operation-level delegates reused across phases.
    /// Per-phase interfaces are resolved by each phase's dependency builder.
    /// </summary>
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        TestConfiguration config,
        ReportPhaseProgressDelegate reportProgress,
        ILogger logger,
        CancellationToken ct)
    {
        // Resolve interfaces needed for shared operation-level delegates
        var clearDatabaseAsync = services.GetRequiredService<Infrastructure.Database.ClearDatabaseDelegate>();
        var clearAllQueuesOp = services.GetRequiredService<ClearAllQueuesDelegate>();
        var publishEventsOp = services.GetRequiredService<PublishEventsDelegate>();
        var eventConsumer = services.GetRequiredService<IEventConsumer>();

        // Shared operation-level delegates (reused across phases)
        SharedPhaseDelegates.ClearDatabaseDelegate clearDatabase = () => clearDatabaseAsync(config.DatabaseName, ct);

        SharedPhaseDelegates.ClearAllQueuesDelegate clearAllQueues = async () =>
        {
            var result = await clearAllQueuesOp(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            return result;
        };

        SharedPhaseDelegates.PublishEventsDelegate publishEvents = (count) => publishEventsOp(count, ct);

        SharedPhaseDelegates.TrackEventsDelegate trackEvents = (count, timeout, reportProgress) =>
            eventConsumer.StartTrackingEventsAsync(
                count,
                timeout,
                reportProgress,
                ct);

        var teardownDeps = TeardownPhaseModule.BuildDependencies(services);

        return new Dependencies(
            RunSetup: BuildRunSetup(services, clearDatabase, clearAllQueues, config, logger, ct),
            RunWarmup: BuildRunWarmup(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
            RunEventTest: BuildRunEventTest(services, trackEvents, publishEvents, config, reportProgress, logger, ct),
            RunApiTest: BuildRunApiTest(services, config, reportProgress, logger, ct),
            RunTeardown: () => TeardownPhaseModule.ExecuteAsync(teardownDeps, ct, logger),
            RunReporting: BuildRunReporting(services, config, logger, ct),
            CleanupResources: () => TeardownPhaseModule.ExecuteAsync(teardownDeps, CancellationToken.None, logger),
            ReportProgress: reportProgress
            );
    }

    private static RunSetupDelegate BuildRunSetup(
        IServiceProvider services,
        SharedPhaseDelegates.ClearDatabaseDelegate clearDatabase,
        SharedPhaseDelegates.ClearAllQueuesDelegate clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = SetupPhaseModule.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
        return (testRunId) => SetupPhaseModule.ExecuteAsync(testRunId, deps, logger);
    }

    private static RunWarmupDelegate BuildRunWarmup(
        SharedPhaseDelegates.TrackEventsDelegate trackEvents,
        SharedPhaseDelegates.PublishEventsDelegate publishEvents,
        SharedPhaseDelegates.ClearDatabaseDelegate clearDatabase,
        SharedPhaseDelegates.ClearAllQueuesDelegate clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = WarmupPhaseModule.BuildDependencies(trackEvents, publishEvents, clearDatabase, clearAllQueues);
        return () => WarmupPhaseModule.ExecuteAsync(config, deps, logger, ct);
    }

    private static RunEventTestDelegate BuildRunEventTest(
        IServiceProvider services,
        SharedPhaseDelegates.TrackEventsDelegate trackEvents,
        SharedPhaseDelegates.PublishEventsDelegate publishEvents,
        TestConfiguration config,
        ReportPhaseProgressDelegate reportProgress,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = EventTestPhaseModule.BuildDependencies(services, trackEvents, publishEvents, config, reportProgress, ct);
        return (serviceProcessId) => EventTestPhaseModule.ExecuteAsync(serviceProcessId, config, deps, logger);
    }

    private static RunApiTestDelegate BuildRunApiTest(
        IServiceProvider services,
        TestConfiguration config,
        ReportPhaseProgressDelegate reportProgress,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = ApiTestPhaseModule.BuildDependencies(services, reportProgress, ct);
        return () => ApiTestPhaseModule.ExecuteAsync(config, deps, logger);
    }

    private static RunReportingDelegate BuildRunReporting(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = ReportingPhaseModule.BuildDependencies(services, config, ct);
        return (testResult) => ReportingPhaseModule.ExecuteAsync(testResult, config, deps, logger);
    }

    // -- Execution (what I do with it) ---------------------------------------------

    public static async Task<Result<TestReport, TestRunError>> RunTestAsync(
        Dependencies deps,
        TestConfiguration configuration,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var testRunId = Guid.NewGuid();
        var testStartTime = DateTime.UtcNow;

        using var logContextTestRunId = LogContext.PushProperty("TestRunId", testRunId);

        logger.LogInformation(
            "Starting performance test run {TestRunId} for service on port {Port}",
            testRunId,
            configuration.ServicePort);

        TestRunError Fail(TestPhase phase, string message)
        {
            logger.LogError("Performance test run {TestRunId} failed in {Phase}: {Message}", testRunId, phase, message);
            deps.ReportProgress(PhaseInfo.Failed(phase, message));
            return new TestRunError(phase, message);
        }

        try
        {
            // Phase 0: Setup
            deps.ReportProgress(PhaseInfo.Starting(TestPhase.Setup, "Starting service discovery and infrastructure setup"));
            var setupResult = await deps.RunSetup(testRunId);
            if (setupResult.IsFailure)
            {
                return Fail(TestPhase.Setup, setupResult.FailureError);
            }

            var serviceProcessId = setupResult.SuccessValue;
            deps.ReportProgress(PhaseInfo.Completed(TestPhase.Setup, $"Setup complete, service PID: {serviceProcessId}"));

            // Phase 0.5: Warmup
            deps.ReportProgress(PhaseInfo.Starting(TestPhase.Warmup, $"Starting warmup with {configuration.WarmupEventCount} events"));
            var warmupStartTime = DateTime.UtcNow;
            var warmupResult = await deps.RunWarmup();
            if (warmupResult.IsFailure)
            {
                return Fail(TestPhase.Warmup, warmupResult.FailureError);
            }

            var warmupEndTime = DateTime.UtcNow;
            deps.ReportProgress(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            deps.ReportProgress(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var eventTestResult = await deps.RunEventTest(serviceProcessId);
            if (eventTestResult.IsFailure)
            {
                return Fail(TestPhase.EventTest, eventTestResult.FailureError);
            }

            var eventTestOutput = eventTestResult.SuccessValue;
            var publishMetrics = eventTestOutput.PublishMetrics;
            var eventTestStartTime = eventTestOutput.StartTime;
            var eventTestEndTime = eventTestOutput.EndTime;
            deps.ReportProgress(PhaseInfo.Completed(TestPhase.EventTest, $"Event test complete: {publishMetrics.EventsPerSecond.Value:F2} events/s"));

            // Phase 2: API Load Test
            deps.ReportProgress(PhaseInfo.Starting(TestPhase.ApiTest, $"Starting API test for {configuration.ApiDuration.Value.TotalSeconds}s"));
            var apiTestResult = await deps.RunApiTest();
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
            deps.ReportProgress(apiPhaseResult);

            var testEndTime = DateTime.UtcNow;

            // Build internal result
            var testResult = new TestResult
            {
                TestRunId = testRunId,
                TestStartTime = testStartTime,
                TestEndTime = testEndTime,
                Configuration = configuration,
                ServiceProcessId = serviceProcessId,
                ServiceProcessName = "service",
                WarmupStartTime = warmupStartTime,
                WarmupEndTime = warmupEndTime,
                EventTestStartTime = eventTestStartTime,
                EventTestEndTime = eventTestEndTime,
                ApiTestStartTime = apiTestStartTime,
                ApiTestEndTime = apiTestEndTime,
                PublishMetrics = publishMetrics,
                ApiLoadTestResult = apiResult,
                ThroughputSamples = Array.Empty<EventThroughputSample>(),
                ProcessMetrics = Array.Empty<ProcessMetrics>(),
                RabbitMqMetrics = Array.Empty<DockerMetrics>(),
                PostgresMetrics = Array.Empty<DockerMetrics>()
            };

            // Phase: Teardown (cancellable during normal flow)
            deps.ReportProgress(PhaseInfo.Starting(TestPhase.Teardown, "Disconnecting publisher and stopping monitors"));
            var teardownResult = await deps.RunTeardown();
            if (teardownResult.IsFailure)
            {
                return Fail(TestPhase.Teardown, teardownResult.FailureError);
            }

            deps.ReportProgress(PhaseInfo.Completed(TestPhase.Teardown, "Teardown complete"));

            // Phase 3: Reporting (collects metrics, generates reports)
            deps.ReportProgress(PhaseInfo.Starting(TestPhase.Reporting, "Starting metrics collection and report generation"));
            var reportingResult = await deps.RunReporting(testResult);
            if (reportingResult.IsFailure)
            {
                return Fail(TestPhase.Reporting, reportingResult.FailureError);
            }

            var testReport = reportingResult.SuccessValue;
            deps.ReportProgress(PhaseInfo.Completed(TestPhase.Reporting, "Report generation complete"));

            logger.LogInformation(
                "Performance test run {TestRunId} completed successfully in {Duration:F2}s",
                testRunId,
                (testEndTime - testStartTime).TotalSeconds);

            return testReport;
        }
        finally
        {
            // Best-effort cleanup -- non-cancellable, must complete even after failure
            try
            {
                await deps.CleanupResources();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed during resource cleanup");
            }
        }
    }
}
