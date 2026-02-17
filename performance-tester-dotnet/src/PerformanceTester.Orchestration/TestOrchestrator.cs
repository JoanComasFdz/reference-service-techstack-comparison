using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Orchestrates complete performance test workflow from setup through reporting.
/// Defines phase-level delegates, bundles them into <see cref="Dependencies"/>,
/// builds them from DI via <see cref="BuildDependencies"/>,
/// and sequences execution in <see cref="RunTestAsync"/>.
/// </summary>
internal static class TestOrchestrator
{
    // -- Delegate definitions (what I need) ----------------------------------------

    /// <summary>
    /// Runs Setup phase: service discovery, infrastructure init, database/queue clearing.
    /// testRunId is generated at runtime by the orchestrator.
    /// </summary>
    public delegate Task<Result<ProcessId, string>> RunSetup(Guid testRunId);

    /// <summary>
    /// Runs Warmup phase: non-measured warmup events and API calls.
    /// All parameters (config, logger, CT, internal delegates) are pre-bound.
    /// </summary>
    public delegate Task<Result<Unit, string>> RunWarmup();

    /// <summary>
    /// Runs Event Test phase: concurrent publish/consume with monitoring.
    /// serviceProcessId comes from Setup output. progress for internal reporting.
    /// </summary>
    public delegate Task<Result<EventTestPhase.Output, string>> RunEventTest(ProcessId serviceProcessId, IProgress<PhaseInfo>? progress);

    /// <summary>
    /// Runs API Load Test phase: k6 load test execution.
    /// progress for internal reporting.
    /// </summary>
    public delegate Task<Result<ApiTestPhase.Output, string>> RunApiTest(IProgress<PhaseInfo>? progress);

    /// <summary>
    /// Runs Teardown phase: disconnect event publisher, stop monitoring services.
    /// Pre-bound with the active CancellationToken -- cancellable during normal flow.
    /// Must complete before reporting can collect metrics.
    /// </summary>
    public delegate Task<Result<Unit, string>> RunTeardown();

    /// <summary>
    /// Best-effort resource cleanup for the finally block.
    /// Pre-bound with CancellationToken.None -- must complete even after cancellation or failure.
    /// Runs the same operations as <see cref="RunTeardown"/> but is not cancellable.
    /// </summary>
    public delegate Task CleanupResources();

    /// <summary>
    /// Runs Reporting phase: metrics collection, report and chart generation.
    /// testResult is assembled at runtime from all phase outputs.
    /// </summary>
    public delegate Task<Result<TestReport, string>> RunReporting(TestResult testResult);

    // -- Dependencies record (bundle of what I need) -------------------------------

    /// <summary>
    /// Phase-level delegates for the test orchestrator.
    /// Each delegate has its internal plumbing (interfaces, config, CT) pre-bound
    /// by <see cref="BuildDependencies"/>.
    /// The orchestrator sequences these and threads inter-phase data.
    /// </summary>
    public record Dependencies(
        RunSetup RunSetup,
        RunWarmup RunWarmup,
        RunEventTest RunEventTest,
        RunApiTest RunApiTest,
        RunTeardown RunTeardown,
        RunReporting RunReporting,
        CleanupResources CleanupResources);

    // -- Factory (how to build what I need from DI) --------------------------------

    /// <summary>
    /// Builds <see cref="Dependencies"/> by composing per-phase dependency classes.
    /// Resolves shared interfaces and creates operation-level delegates reused across phases.
    /// Per-phase interfaces are resolved by each phase's dependency builder.
    /// </summary>
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        // Resolve interfaces needed for shared operation-level delegates
        var database = services.GetRequiredService<IDatabase>();
        var rabbitMq = services.GetRequiredService<IRabbitMQ>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();
        var eventConsumer = services.GetRequiredService<IEventConsumer>();

        // Shared operation-level delegates (reused across phases)
        PhasesToolbox.ClearDatabase clearDatabase = () => database.ClearDatabaseAsync(config.DatabaseName.Value, ct);

        PhasesToolbox.ClearAllQueues clearAllQueues = async () =>
        {
            var result = await rabbitMq.ClearAllQueuesAsync(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            return result;
        };

        PhasesToolbox.PublishEvents publishEvents = (count) => eventPublisher.PublishEventsAsync(count, ct);

        PhasesToolbox.TrackEvents trackEvents = (count, timeout, progress) => eventConsumer.StartTrackingEventsAsync(count, timeout, progress, ct);

        var teardownDeps = TeardownPhase.BuildDependencies(services);

        return new Dependencies(
            RunSetup: BuildRunSetup(services, clearDatabase, clearAllQueues, config, logger, ct),
            RunWarmup: BuildRunWarmup(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
            RunEventTest: BuildRunEventTest(services, trackEvents, publishEvents, config, logger, ct),
            RunApiTest: BuildRunApiTest(services, config, logger, ct),
            RunTeardown: () => TeardownPhase.ExecuteAsync(teardownDeps, ct, logger),
            RunReporting: ReportingPhaseDependencies.Build(services, config, logger, ct),
            CleanupResources: () => TeardownPhase.ExecuteAsync(teardownDeps, CancellationToken.None, logger));
    }

    private static RunSetup BuildRunSetup(
        IServiceProvider services,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = SetupPhase.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
        return (testRunId) => SetupPhase.ExecuteAsync(testRunId, deps, logger);
    }

    private static RunWarmup BuildRunWarmup(
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = WarmupPhase.BuildDependencies(trackEvents, publishEvents, clearDatabase, clearAllQueues);
        return () => WarmupPhase.ExecuteAsync(config, deps, logger, ct);
    }

    private static RunEventTest BuildRunEventTest(
        IServiceProvider services,
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = EventTestPhase.BuildDependencies(services, trackEvents, publishEvents, config, ct);
        return (serviceProcessId, progress) => EventTestPhase.ExecuteAsync(serviceProcessId, deps, progress, logger);
    }

    private static RunApiTest BuildRunApiTest(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = ApiTestPhase.BuildDependencies(services, config, ct);
        return (progress) => ApiTestPhase.ExecuteAsync(deps, progress, logger);
    }

    // -- Execution (what I do with it) ---------------------------------------------

    public static async Task<Result<TestReport, TestRunFailure>> RunTestAsync(
        Dependencies deps,
        TestConfiguration configuration,
        IProgress<PhaseInfo>? progress,
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

        TestRunFailure Fail(TestPhase phase, string message)
        {
            logger.LogError("Performance test run {TestRunId} failed in {Phase}: {Message}", testRunId, phase, message);
            progress?.Report(PhaseInfo.Failed(phase, message));
            return new TestRunFailure(phase, message);
        }

        try
        {
            // Phase 0: Setup
            progress?.Report(PhaseInfo.Starting(TestPhase.Setup, "Starting service discovery and infrastructure setup"));
            var setupResult = await deps.RunSetup(testRunId);
            if (setupResult.IsFailure)
            {
                return Fail(TestPhase.Setup, setupResult.FailureError);
            }

            var serviceProcessId = setupResult.SuccessValue;
            progress?.Report(PhaseInfo.Completed(TestPhase.Setup, $"Setup complete, service PID: {serviceProcessId}"));

            // Phase 0.5: Warmup
            progress?.Report(PhaseInfo.Starting(TestPhase.Warmup, $"Starting warmup with {configuration.WarmupEventCount} events"));
            var warmupStartTime = DateTime.UtcNow;
            var warmupResult = await deps.RunWarmup();
            if (warmupResult.IsFailure)
            {
                return Fail(TestPhase.Warmup, warmupResult.FailureError);
            }

            var warmupEndTime = DateTime.UtcNow;
            progress?.Report(PhaseInfo.Completed(TestPhase.Warmup, "Warmup complete"));

            // Phase 1: Event Throughput Test (CONCURRENT publish/consume)
            progress?.Report(PhaseInfo.Starting(TestPhase.EventTest, $"Starting event test with {configuration.EventCount} events"));
            var eventTestResult = await deps.RunEventTest(serviceProcessId, progress);
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
            var apiTestResult = await deps.RunApiTest(progress);
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
                ServiceProcessId = serviceProcessId.Value,
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
                PostgresMetrics = Array.Empty<DockerMetrics>(),
                SystemInfo = null
            };

            // Phase: Teardown (cancellable during normal flow)
            progress?.Report(PhaseInfo.Starting(TestPhase.Teardown, "Disconnecting publisher and stopping monitors"));
            var teardownResult = await deps.RunTeardown();
            if (teardownResult.IsFailure)
            {
                return Fail(TestPhase.Teardown, teardownResult.FailureError);
            }

            progress?.Report(PhaseInfo.Completed(TestPhase.Teardown, "Teardown complete"));

            // Phase 3: Reporting (collects metrics, generates reports)
            progress?.Report(PhaseInfo.Starting(TestPhase.Reporting, "Starting metrics collection and report generation"));
            var reportingResult = await deps.RunReporting(testResult);
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
            // Best-effort cleanup -- non-cancellable, must complete even after failure
            try { await deps.CleanupResources(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed during resource cleanup"); }
        }
    }
}
