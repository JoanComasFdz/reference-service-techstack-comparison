using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Orchestrates complete performance test workflow from setup through reporting.
/// Receives all capabilities as pre-bound phase delegates via <see cref="OrchestratorDeps"/>.
/// Sequences phases, threads inter-phase data, and frames progress reporting.
/// </summary>
internal static class TestOrchestrator
{
    public static async Task<Result<TestReport, TestRunFailure>> RunTestAsync(
        OrchestratorDeps deps,
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
            // Best-effort cleanup — non-cancellable, must complete even after failure
            try { await deps.CleanupResources(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed during resource cleanup"); }
        }
    }
}
