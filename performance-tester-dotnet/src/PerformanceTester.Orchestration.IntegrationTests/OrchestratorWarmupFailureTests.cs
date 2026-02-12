using Xunit;
using Xunit.Abstractions;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

namespace PerformanceTester.Orchestration.IntegrationTests;

/// <summary>
/// Collection definition to disable parallel execution for warmup failure tests.
/// Required because tests share the ConfigurableReferenceService HTTP listener.
/// </summary>
[CollectionDefinition("WarmupFailureTests", DisableParallelization = true)]
public class WarmupFailureTestsCollection { }

/// <summary>
/// Integration tests for warmup phase failure scenarios.
/// Tests validate that the orchestrator properly handles various warmup failure modes:
/// - Consumer timeout (no warmup events received)
/// - Partial warmup completion (warmup succeeds, test phase times out)
/// - API failures during warmup (non-fatal, should continue)
/// - Database clear failures during warmup (fatal, should abort)
/// </summary>
/// <remarks>
/// These tests must run sequentially because they share ConfigurableReferenceService resources.
/// The [Collection] attribute ensures xUnit runs them one at a time.
/// Port range: 9960-9969
/// </remarks>
[Collection("WarmupFailureTests")]
public sealed class OrchestratorWarmupFailureTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Test 1.2: Verifies that warmup consumer timeout aborts the entire test with a clear error message.
    /// Service ACKs incoming events but never publishes warmup response events.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenWarmupConsumerTimesOut_ShouldAbortWithClearMessage()
    {
        // Arrange
        const int testPort = 9960;

        // Configure service to NOT publish any response events (complete silence)
        System.ConfigurableReferenceService.ConfigureNoResponse();

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(5)
            .WithWarmupInactivityTimeout(TimeSpan.FromSeconds(2))
            .WithEventCount(100)  // Won't reach this
            .WithApiDuration(TimeSpan.FromSeconds(5))  // Won't reach this
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config);
            });

            // Should mention warmup or inactivity in the error message
            Assert.True(
                exception.Message.Contains("warmup", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("inactivity", StringComparison.OrdinalIgnoreCase),
                $"Exception should mention warmup or inactivity. Actual: {exception.Message}");

            Output.WriteLine($"Test passed. Exception message: {exception.Message}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 1.3: Verifies that if warmup succeeds but test events never arrive,
    /// the test times out in the test phase (not warmup).
    /// Service publishes warmup events, then stops publishing.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenServiceOnlyPublishesWarmupEvents_ShouldTimeoutOnTestPhase()
    {
        // Arrange
        const int testPort = 9961;
        const int warmupEventCount = 5;
        const int testEventCount = 10;

        // Configure service to ONLY publish warmup events, then stop
        System.ConfigurableReferenceService.ConfigureWarmupOnly(warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithWarmupInactivityTimeout(TimeSpan.FromSeconds(3))
            .WithEventCount(testEventCount)
            .WithInactivityTimeout(TimeSpan.FromSeconds(2))  // Short timeout for test phase
            .WithApiDuration(TimeSpan.FromSeconds(5))  // Won't reach this
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config);
            });

            // Should timeout during TEST phase showing 0 of expected test events received
            // The progress should show "0/10" (test events, not warmup events)
            Assert.Contains("0", exception.Message);
            Assert.Contains(testEventCount.ToString(), exception.Message);

            Output.WriteLine($"Test passed. Exception message: {exception.Message}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 1.4: Verifies that warmup API call failures are logged but don't abort the test.
    /// Per production behavior, warmup API failures are non-fatal.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenWarmupApiCallsFail_ShouldContinueToTestPhase()
    {
        // Arrange
        const int testPort = 9962;
        const int warmupEventCount = 5;
        const int testEventCount = 10;

        // Configure service to respond to events but return 500 for HTTP
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: testEventCount,
            warmupEventCount: warmupEventCount);
        System.ConfigurableReferenceService.ConfigureHttpResponse(statusCode: 500);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var resultsFolder = $"./test-results-warmup-api-fail-{Guid.NewGuid():N}";

        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithWarmupApiCallCount(10)  // 10 warmup API calls, all will fail
            .WithEventCount(testEventCount)
            .WithApiDuration(TimeSpan.FromSeconds(3))
            .WithMaxConsecutiveApiFailures(5)  // Allow some failures during API phase
            .WithResultsFolder(resultsFolder)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act - Test should complete (not throw) because warmup API failures are not fatal
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert - Verify test actually ran through all phases
            Assert.True(report.Results.Phase1Publish.DurationSeconds > 0,
                "Event publish phase should have run");
            Assert.True(report.Results.Phase2Consume.DurationSeconds > 0,
                "Event consume phase should have run");
            Assert.True(report.Results.Phase3Api.DurationSeconds > 0,
                "API phase should have run");

            // API phase should show errors (from the 500 responses)
            Assert.True(report.Results.Phase3Api.ErrorCount > 0,
                "API phase should have recorded errors from 500 responses");

            Output.WriteLine($"Test passed. API errors recorded: {report.Results.Phase3Api.ErrorCount}");
            Output.WriteLine($"Phases completed - Publish: {report.Results.Phase1Publish.DurationSeconds:F2}s, " +
                           $"Consume: {report.Results.Phase2Consume.DurationSeconds:F2}s, " +
                           $"API: {report.Results.Phase3Api.DurationSeconds:F2}s");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);

            // Clean up results folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// Test 1.5: Verifies that database clear failure during warmup aborts the test.
    /// Uses an invalid/non-existent database name to trigger the failure.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenWarmupDatabaseClearFails_ShouldAbort()
    {
        // Arrange
        const int testPort = 9963;
        const int warmupEventCount = 5;

        // Configure service normally (it won't matter - test will fail during DB setup)
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 10,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        // Use a non-existent database name to trigger failure during database clear
        var config = new TestConfigurationBuilder()
            .WithServicePort(testPort)
            .WithWarmupEventCount(warmupEventCount)
            .WithEventCount(10)
            .WithApiDuration(TimeSpan.FromSeconds(3))
            .WithDatabaseName("nonexistent_database_xyz_" + Guid.NewGuid().ToString("N"))
            .Build();

        try
        {
            // Act & Assert - Should fail during setup/warmup phase with database error
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config);
            });

            // The exception should be database-related (Npgsql exception or similar)
            Output.WriteLine($"Test passed. Exception type: {exception.GetType().Name}");
            Output.WriteLine($"Exception message: {exception.Message}");

            // Verify it's a database-related exception (Postgres-specific or containing "database")
            var isDbRelated = exception.GetType().Name.Contains("Postgres") ||
                             exception.GetType().Name.Contains("Npgsql") ||
                             exception.Message.Contains("database", StringComparison.OrdinalIgnoreCase) ||
                             exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

            Assert.True(isDbRelated,
                $"Expected a database-related exception. Got: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
