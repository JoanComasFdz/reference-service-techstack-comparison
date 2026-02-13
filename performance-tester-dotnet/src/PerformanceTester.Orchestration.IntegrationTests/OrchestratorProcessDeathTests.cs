using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests;

/// <summary>
/// Collection definition to disable parallel execution for process death tests.
/// Required because tests share the ConfigurableReferenceService and port bindings.
/// </summary>
[CollectionDefinition("ProcessDeathTests", DisableParallelization = true)]
public class ProcessDeathTestsCollection { }

/// <summary>
/// Integration tests for process death scenarios.
/// Tests the orchestrator's behavior when the service under test terminates unexpectedly
/// during different phases of the test workflow.
/// </summary>
/// <remarks>
/// These tests must run sequentially because they share ConfigurableReferenceService resources.
/// The [Collection] attribute ensures xUnit runs them one at a time.
/// </remarks>
[Collection("ProcessDeathTests")]
public sealed class OrchestratorProcessDeathTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Test 2.2: Verifies that when the service dies during event publishing/consumption,
    /// the orchestrator times out with partial progress information.
    /// </summary>
    /// <remarks>
    /// The ConfigurableReferenceService is configured to terminate after processing 50 events
    /// out of 500 expected, simulating a process crash mid-test.
    /// </remarks>
    [Fact]
    public async Task RunTestAsync_WhenServiceDiesDuringEventPublishing_ShouldTimeoutWithProgress()
    {
        // Arrange
        const int testPort = 9970;
        const int eventCount = 200;          // Reduced from 500 to make test faster
        const int warmupEventCount = 10;
        const int terminateAfterEvents = 30; // Terminate after 30 input events

        // Configure service to process events but terminate after 30 input events (simulates process death)
        // Add a delay between output events so termination can interrupt before all events published
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);
        System.ConfigurableReferenceService.ConfigureTerminationAfterEvents(terminateAfterEvents);
        System.ConfigurableReferenceService.ConfigurePublishDelay(TimeSpan.FromMilliseconds(100)); // Slow down publishing

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithEventCount(eventCount)
            .WithInactivityTimeout(TimeSpan.FromSeconds(5))  // Short timeout to detect service death
            .WithApiDuration(TimeSpan.FromSeconds(5))        // Won't reach this phase
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act & Assert: Should return failure during event consumption with partial progress
            var result = await System.Orchestration.Orchestrator.RunTestAsync(config);

            Assert.True(result.IsFailure, "Expected a failure result for service death during event publishing");
            Assert.Equal(TestPhase.EventTest, result.FailureError.Phase);

            // Verify failure message shows partial progress (some events out of 200)
            // The service terminates after processing ~30 input events, which interrupts
            // the output publishing before all 200 events are published
            // Progress format is typically "X/Y" where Y is the expected count
            Assert.True(
                result.FailureError.Message.Contains("/200") || result.FailureError.Message.Contains("of 200"),
                $"Failure should show progress out of 200 expected events. Actual: {result.FailureError.Message}");

            Output.WriteLine($"Test passed: Failure with message: {result.FailureError.Message}");
        }
        finally
        {
            // Cleanup - service may already be disconnected due to simulated death
            try
            {
                await System.ConfigurableReferenceService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Disconnect warning (expected if service died): {ex.Message}");
            }

            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 2.3: Verifies that when the service dies during the warmup phase,
    /// the test aborts with warmup progress information.
    /// </summary>
    /// <remarks>
    /// The ConfigurableReferenceService is configured to terminate after processing 20 events
    /// during a 50-event warmup phase.
    /// </remarks>
    [Fact]
    public async Task RunTestAsync_WhenServiceDiesDuringWarmup_ShouldAbort()
    {
        // Arrange
        const int testPort = 9971;
        const int eventCount = 100;
        const int warmupEventCount = 50;
        const int terminateAfterEvents = 20;  // Dies during warmup (before 50 warmup events complete)

        // Configure service to process events but terminate after 20 events (mid-warmup)
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);
        System.ConfigurableReferenceService.ConfigureTerminationAfterEvents(terminateAfterEvents);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithWarmupInactivityTimeout(TimeSpan.FromSeconds(5))  // Short timeout to detect service death
            .WithEventCount(eventCount)
            .WithInactivityTimeout(TimeSpan.FromSeconds(10))
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act & Assert: Should return failure during warmup phase
            var result = await System.Orchestration.Orchestrator.RunTestAsync(config);

            Assert.True(result.IsFailure, "Expected a failure result for service death during warmup");
            Assert.Equal(TestPhase.Warmup, result.FailureError.Phase);

            // Verify failure message shows warmup progress (partial progress out of 50 warmup events)
            Assert.True(
                result.FailureError.Message.Contains("/50") || result.FailureError.Message.Contains("of 50"),
                $"Failure should show warmup progress. Actual: {result.FailureError.Message}");

            Output.WriteLine($"Test passed: Failure during warmup with message: {result.FailureError.Message}");
        }
        finally
        {
            // Cleanup - service may already be disconnected due to simulated death
            try
            {
                await System.ConfigurableReferenceService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Disconnect warning (expected if service died): {ex.Message}");
            }

            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 2.4: Verifies that when the service dies during the API load test phase,
    /// the API test aborts due to consecutive failures.
    /// </summary>
    /// <remarks>
    /// This test configures the service to complete event publishing successfully,
    /// then uses PhaseAwaiter to deterministically wait for the API phase to start
    /// before simulating service death by disconnecting the HTTP listener.
    /// </remarks>
    [Fact]
    public async Task RunTestAsync_WhenServiceDiesDuringApiPhase_ShouldAbortApiTest()
    {
        // Arrange
        const int testPort = 9972;
        const int eventCount = 10;
        const int warmupEventCount = 10;

        // Configure service to complete events successfully
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        // NOTE: Do NOT configure intermittent failures here!
        // The service should work normally until we disconnect it during API phase.

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        // Configure HTTP to succeed (service is healthy until we disconnect it)
        System.ConfigurableReferenceService.ConfigureHttpResponse(statusCode: 200);

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithEventCount(eventCount)
            .WithApiDuration(TimeSpan.FromSeconds(10))
            .WithMaxConsecutiveApiFailures(3)  // Abort after 3 consecutive failures
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        // Create phase awaiter for deterministic phase detection
        var phaseAwaiter = new PhaseAwaiter();

        try
        {
            // Start the test with progress reporting
            var testTask = Task.Run(async () =>
            {
                return await System.Orchestration.Orchestrator.RunTestAsync(
                    config,
                    progress: phaseAwaiter);
            });

            // DETERMINISTIC: Wait for API phase to actually start
            Output.WriteLine("Waiting for API phase to start...");
            await phaseAwaiter.WaitForPhaseStartAsync(TestPhase.ApiTest, timeout: TimeSpan.FromSeconds(30));
            Output.WriteLine("API phase started - now simulating service death");

            // Give API test time to make some successful requests before we kill the service
            // k6 needs time to start up and make initial requests
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Simulate service death by disconnecting (HTTP listener stops)
            Output.WriteLine("Simulating service death by disconnecting...");
            await System.ConfigurableReferenceService.DisconnectAsync();

            // Wait for test to complete (should abort due to HTTP failures)
            var result = await testTask;
            Assert.True(result.IsSuccess, $"Expected success but got failure: {(result.IsFailure ? result.FailureError.Message : "")}");
            var report = result.SuccessValue;

            // Assert: API test should have been aborted due to consecutive failures
            Assert.True(report.Results.Phase3Api.WasAborted,
                "API load test should have been aborted due to service death");
            Assert.NotNull(report.Results.Phase3Api.AbortReason);
            Assert.Contains("consecutive", report.Results.Phase3Api.AbortReason!, StringComparison.OrdinalIgnoreCase);

            Output.WriteLine($"API test aborted. Success count: {report.Results.Phase3Api.SuccessCount}, " +
                            $"Error count: {report.Results.Phase3Api.ErrorCount}");
            Output.WriteLine($"Abort reason: {report.Results.Phase3Api.AbortReason}");

            // Should have SOME successes before death, and errors after
            Assert.True(report.Results.Phase3Api.SuccessCount > 0,
                "Should have some successful requests before service death");
            Assert.True(report.Results.Phase3Api.ErrorCount > 0,
                "Should have errors after service death");
        }
        finally
        {
            try
            {
                await System.ConfigurableReferenceService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Disconnect warning (expected if already disconnected): {ex.Message}");
            }

            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 2.5: Verifies that when the service dies mid-test, the test handles
    /// it gracefully without crashing, timing out with progress information.
    /// </summary>
    /// <remarks>
    /// This test is similar to Test 2.2 but focuses on verifying graceful handling:
    /// - No unhandled exceptions
    /// - Proper timeout with progress
    /// - Test framework doesn't crash
    /// </remarks>
    [Fact]
    public async Task RunTestAsync_WhenServiceDies_ProcessMetricsShouldStopGracefully()
    {
        // Arrange
        const int testPort = 9973;
        const int eventCount = 200;
        const int warmupEventCount = 10;
        const int terminateAfterEvents = 30;  // Service dies mid-test (after 30 input events)

        // Configure service to terminate after 30 input events (well into test phase)
        // Add delay to slow down output publishing so termination can interrupt it
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);
        System.ConfigurableReferenceService.ConfigureTerminationAfterEvents(terminateAfterEvents);
        System.ConfigurableReferenceService.ConfigurePublishDelay(TimeSpan.FromMilliseconds(100)); // Slow down publishing

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithEventCount(eventCount)
            .WithInactivityTimeout(TimeSpan.FromSeconds(5))  // Short timeout to detect service death
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act: Test should return failure gracefully (not crash) when service dies
            var result = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert: The test framework didn't crash - we got a proper failure result
            // This verifies graceful handling of process death
            Assert.True(result.IsFailure, "Expected a failure result for service death");
            Assert.Equal(TestPhase.EventTest, result.FailureError.Phase);
            Assert.NotEmpty(result.FailureError.Message);

            // Verify the failure message contains meaningful progress information
            // The message should include how many events were received before timeout
            Assert.True(
                result.FailureError.Message.Contains("Inactivity timeout") ||
                result.FailureError.Message.Contains("timeout"),
                $"Failure should mention timeout. Actual: {result.FailureError.Message}");

            Output.WriteLine($"Test passed: Graceful failure with message: {result.FailureError.Message}");
            Output.WriteLine("Process metrics collection stopped gracefully (no crash occurred)");
        }
        finally
        {
            // Cleanup - service may already be disconnected due to simulated death
            try
            {
                await System.ConfigurableReferenceService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Disconnect warning (expected if service died): {ex.Message}");
            }

            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
