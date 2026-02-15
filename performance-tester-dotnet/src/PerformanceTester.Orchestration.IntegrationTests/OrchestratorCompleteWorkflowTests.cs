using JoanComasFdz.AssertingThat;
using PerformanceTester.Orchestration;  // For TestPhase, PhaseState
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests;

public sealed class OrchestratorCompleteWorkflowTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Comprehensive integration test that verifies the entire orchestration workflow
    /// from service discovery through all test phases to final report generation.
    /// This test consolidates all happy-path scenarios with phase-by-phase assertions.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_CompleteWorkflow_ShouldExecuteAllPhasesSuccessfully()
    {
        // ====================================================================
        // ARRANGE
        // ====================================================================

        const int testPort = 9990;
        const int eventCount = 500;
        const int warmupEventCount = 50;  // Match default from TestConfigurationBuilder

        // Configure ConfigurableReferenceService to handle full workflow
        // It needs to respond to warmup events AND test events
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        // Create unique results folder for this test run
        var resultsFolder = $"./test-results-{Guid.NewGuid():N}";

        try
        {
            var config = new TestConfigurationBuilder()
                .WithEventCount(eventCount)
                .WithWarmupEventCount(warmupEventCount)
                .WithApiWorkers(1)
                .WithApiDuration(TimeSpan.FromSeconds(10))  // Match expected range 9-12 seconds
                .WithServicePort(testPort)
                .WithResultsFolder(resultsFolder)
                .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
                .Build();

            // Arrange: Create phase awaiter for deterministic phase tracking
            var phaseAwaiter = new PhaseAwaiter();

            // ====================================================================
            // ACT
            // ====================================================================

            var result = await System.Orchestration.RunTestAsync(config, progress: phaseAwaiter);
            Assert.True(result.IsSuccess, $"Expected success but got failure: {(result.IsFailure ? result.FailureError.Message : "")}");
            var report = result.SuccessValue;

            // ====================================================================
            // ASSERT - PHASE 0: SETUP (Service Discovery)
            // ====================================================================

            // Verify service was discovered
            Assert.True(report.MonitoredProcess.Pid > 0,
                "Service PID should be discovered");
            // NOTE: Process name will be the test runner process, not "dotnet9AotReferenceService"
            Assert.NotEmpty(report.MonitoredProcess.Name);

            Output.WriteLine($"✓ Phase 0 (Setup): Service discovered (PID: {report.MonitoredProcess.Pid})");

            // ====================================================================
            // ASSERT - PHASE 0.5: WARMUP
            // ====================================================================

            // Warmup runs successfully (no exception thrown)
            // Warmup data is discarded, so no direct assertions on warmup metrics
            // Success indicated by reaching event test phase
            Assert.True(report.Results.Phase1Publish.DurationSeconds > 0,
                "Event publish phase should have run (indicates warmup completed)");

            Output.WriteLine("✓ Phase 0.5 (Warmup): Completed successfully (data discarded)");

            // ====================================================================
            // ASSERT - PHASE 1 & 2: EVENT TEST (Publish + Consume)
            // ====================================================================

            // Verify event count configuration
            Assert.Equal(eventCount, report.Configuration.NumEvents);

            // Verify publishing phase completed
            Assert.True(report.Results.Phase1Publish.DurationSeconds > 0,
                "Event publish phase should have duration");
            Assert.True(report.Results.Phase1Publish.ThroughputEventsPerSec > 0,
                "Event publish throughput should be positive");

            // Verify consuming phase completed
            Assert.True(report.Results.Phase2Consume.DurationSeconds > 0,
                "Event consume phase should have duration");
            Assert.True(report.Results.Phase2Consume.ThroughputEventsPerSec > 0,
                "Event consume throughput should be positive");

            // Verify concurrent execution (Phase2 starts before Phase1 ends)
            Assert.True(report.PhaseTimestamps.Phase2Start < report.PhaseTimestamps.Phase1End,
                "Consumer should start before publisher completes (concurrent execution)");

            // Verify publishing completes faster than consuming (expected behavior)
            Assert.True(report.Results.Phase1Publish.DurationSeconds < report.Results.Phase2Consume.DurationSeconds,
                "Publishing should complete faster than consuming");

            // Verify throughput samples collected
            Assert.NotEmpty(report.EventsThroughputSamples);

            // Verify samples have valid timestamps
            var eventSamples = report.EventsThroughputSamples.ToList();
            Assert.All(eventSamples, s => Assert.True(s.Timestamp > DateTime.MinValue,
                "All throughput samples should have valid timestamps"));

            Output.WriteLine($"✓ Phase 1 (Publish): {report.Results.Phase1Publish.ThroughputEventsPerSec:F2} events/sec");
            Output.WriteLine($"✓ Phase 2 (Consume): {report.Results.Phase2Consume.ThroughputEventsPerSec:F2} events/sec");
            Output.WriteLine($"✓ Event Test: Collected {eventSamples.Count} throughput samples");

            // ====================================================================
            // ASSERT - PHASE 3: API LOAD TEST
            // ====================================================================

            // Verify all phases completed in correct order
            phaseAwaiter.AssertPhasesReceivedInOrder(
                (TestPhase.Setup, PhaseState.Starting),
                (TestPhase.Setup, PhaseState.Completed),
                (TestPhase.Warmup, PhaseState.Starting),
                (TestPhase.Warmup, PhaseState.Completed),
                (TestPhase.EventTest, PhaseState.Starting),
                (TestPhase.EventTest, PhaseState.Completed),
                (TestPhase.ApiTest, PhaseState.Starting),
                (TestPhase.ApiTest, PhaseState.Completed),
                (TestPhase.Teardown, PhaseState.Starting),
                (TestPhase.Teardown, PhaseState.Completed),
                (TestPhase.Reporting, PhaseState.Starting),
                (TestPhase.Reporting, PhaseState.Completed));

            // Verify API metrics collected
            Assert.True(report.Results.Phase3Api.TotalRequests > 0,
                "API test should have processed requests");
            Assert.True(report.Results.Phase3Api.ThroughputCallsPerSec > 0,
                "API throughput should be positive");

            // Verify no errors during API test
            Assert.Equal(100.0, report.Results.Phase3Api.SuccessPercentage);
            Assert.Equal(0, report.Results.Phase3Api.ErrorCount);

            // Verify API throughput samples collected
            Assert.NotEmpty(report.ApiThroughputSamples);

            Output.WriteLine($"✓ Phase 3 (API Load): {report.Results.Phase3Api.ThroughputCallsPerSec:F2} req/sec");
            Output.WriteLine($"✓ API Load: {report.Results.Phase3Api.TotalRequests} total requests, 0 errors");

            // ====================================================================
            // ASSERT - RESOURCE MONITORING (All Phases)
            // ====================================================================

            // Verify process resource metrics collected
            Assert.NotEmpty(report.ProcessResourceSamples);

            // Verify RabbitMQ metrics collected
            Assert.NotEmpty(report.RabbitMqResourceSamples);

            // Verify PostgreSQL metrics collected
            Assert.NotEmpty(report.PostgresResourceSamples);

            Output.WriteLine($"✓ Resource Monitoring: Process ({report.ProcessResourceSamples.Count} samples), " +
                            $"RabbitMQ ({report.RabbitMqResourceSamples.Count} samples), " +
                            $"PostgreSQL ({report.PostgresResourceSamples.Count} samples)");

            // ====================================================================
            // ASSERT - PHASE 4: REPORTING
            // ====================================================================

            // Verify system info collected
            Assert.NotNull(report.System);
            Assert.NotEmpty(report.System.Os);
            Assert.NotEmpty(report.System.Cpu.Model);
            Assert.True(report.System.Ram.TotalGb > 0,
                "System total memory should be positive");

            // Verify all report files generated
            var reportFiles = Directory.GetFiles(resultsFolder, "test-report-*");

            // Expected files:
            // 1. Main report JSON
            // 2. Events throughput JSON
            // 3. API throughput JSON
            // 4. Resource metrics JSON
            // 5. System metrics JSON
            // 6. RabbitMQ metrics JSON
            // 7. PostgreSQL metrics JSON
            // 8. Chart PNG
            Assert.True(
                reportFiles.Length >= 8,
                $"Expected at least 8 report files, found {reportFiles.Length}");

            Output.WriteLine($"✓ Phase 4 (Reporting): Generated {reportFiles.Length} report files");
            Output.WriteLine($"✓ System Info: {report.System.Os}, " +
                            $"{report.System.Cpu.Model}, {report.System.Ram.TotalGb} GB RAM");

            // ====================================================================
            // ASSERT - OVERALL WORKFLOW
            // ====================================================================

            // Verify total runtime is at least API duration
            Assert.True(report.TotalRuntimeSeconds >= 10,
                "Total runtime should be at least API test duration");

            // Verify all phases completed (using custom assertion)
            await Asserting.That(report).CompletedAllPhases();

            Output.WriteLine($"✓ Complete Workflow: Total runtime {report.TotalRuntimeSeconds:F2}s");
            Output.WriteLine("✓ All phases completed successfully!");

            // ====================================================================
            // CLEANUP
            // ====================================================================

            // Remove test results folder
            Directory.Delete(resultsFolder, recursive: true);
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();

            // CRITICAL: Purge ConfigurableReferenceService queue to prevent stale events
            // from affecting subsequent tests. Events published to the shared exchange
            // are routed to ALL bound queues, including this one if it exists.
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
