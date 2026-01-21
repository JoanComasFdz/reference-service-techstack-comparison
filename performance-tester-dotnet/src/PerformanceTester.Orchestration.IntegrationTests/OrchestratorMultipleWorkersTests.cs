using JoanComasFdz.AssertingThat;
using Xunit;
using Xunit.Abstractions;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

namespace PerformanceTester.Orchestration.IntegrationTests;

/// <summary>
/// Collection definition to disable parallel execution for multiple workers tests.
/// Required because tests share the ConfigurableReferenceService HTTP listener
/// and each test uses different ports that need cleanup.
/// </summary>
[CollectionDefinition("MultipleWorkersTests", DisableParallelization = true)]
public class MultipleWorkersTestsCollection { }

/// <summary>
/// Integration tests for API load testing with multiple concurrent workers.
/// Tests verify that worker count configuration affects API throughput.
/// </summary>
/// <remarks>
/// These tests must run sequentially because they share ConfigurableReferenceService resources.
/// The [Collection] attribute ensures xUnit runs them one at a time.
/// Port range: 9980-9989
/// </remarks>
[Collection("MultipleWorkersTests")]
public sealed class OrchestratorMultipleWorkersTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Test 3.2: Verifies that configuring 2 API workers increases request count.
    /// With 2 workers over 5 seconds, expect at least 100 requests (10 req/s per worker conservative estimate).
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WithTwoApiWorkers_ShouldIncreaseRequestCount()
    {
        // Arrange
        const int testPort = 9980;
        const int eventCount = 100;
        const int warmupEventCount = 10;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        var config = new TestConfigurationBuilder()
            .WithEventCount(eventCount)
            .WithWarmupEventCount(warmupEventCount)
            .WithApiWorkers(2)
            .WithApiDuration(TimeSpan.FromSeconds(5))
            .WithServicePort(testPort)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
            await System.WaitForServiceHealthyAsync(port: testPort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert - Test completes all phases successfully
            await Asserting.That(System.Orchestration.Orchestrator)
                .CompletedAllPhases(report);

            // Assert - With 2 workers at 5s duration, expect at least 100 requests
            // (Conservative: 10 req/s per worker x 2 workers x 5s = 100)
            Assert.True(report.Results.Phase3Api.TotalRequests >= 100,
                $"Expected at least 100 requests with 2 workers, got {report.Results.Phase3Api.TotalRequests}");

            // Assert - 100% success rate
            Assert.Equal(100.0, report.Results.Phase3Api.SuccessPercentage);

            Output.WriteLine($"API test with 2 workers completed: {report.Results.Phase3Api.TotalRequests} total requests");
            Output.WriteLine($"Throughput: {report.Results.Phase3Api.ThroughputCallsPerSec:F2} req/sec");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 3.3: Verifies that configuring 5 API workers scales proportionally.
    /// With 5 workers over 5 seconds, expect at least 250 requests (10 req/s per worker conservative estimate).
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WithFiveApiWorkers_ShouldScaleProportionally()
    {
        // Arrange
        const int testPort = 9981;
        const int eventCount = 100;
        const int warmupEventCount = 10;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        var config = new TestConfigurationBuilder()
            .WithEventCount(eventCount)
            .WithWarmupEventCount(warmupEventCount)
            .WithApiWorkers(5)
            .WithApiDuration(TimeSpan.FromSeconds(5))
            .WithServicePort(testPort)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
            await System.WaitForServiceHealthyAsync(port: testPort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert - Test completes successfully
            await Asserting.That(System.Orchestration.Orchestrator)
                .CompletedAllPhases(report);

            // Assert - With 5 workers at 5s duration, expect at least 250 requests
            // (Conservative: 10 req/s per worker x 5 workers x 5s = 250)
            Assert.True(report.Results.Phase3Api.TotalRequests >= 250,
                $"Expected at least 250 requests with 5 workers, got {report.Results.Phase3Api.TotalRequests}");

            Output.WriteLine($"API test with 5 workers completed: {report.Results.Phase3Api.TotalRequests} total requests");
            Output.WriteLine($"Throughput: {report.Results.Phase3Api.ThroughputCallsPerSec:F2} req/sec");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 3.4: Verifies that multiple workers with intermittent errors report errors correctly.
    /// Configures ~20% error rate (fail every 5th request) and verifies error percentage.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WithMultipleWorkersAndErrors_ShouldReportErrorsCorrectly()
    {
        // Arrange
        const int testPort = 9982;
        const int eventCount = 100;
        const int warmupEventCount = 10;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        // Configure intermittent failures (fail every 5th request = ~20% error rate)
        System.ConfigurableReferenceService.ConfigureIntermittentFailures(failEveryNthRequest: 5);

        var config = new TestConfigurationBuilder()
            .WithEventCount(eventCount)
            .WithWarmupEventCount(warmupEventCount)
            .WithApiWorkers(3)
            .WithApiDuration(TimeSpan.FromSeconds(5))
            .WithMaxConsecutiveApiFailures(0) // Disable abort to allow test to complete
            .WithServicePort(testPort)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
            await System.WaitForServiceHealthyAsync(port: testPort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert - Test completes (no abort)
            await Asserting.That(System.Orchestration.Orchestrator)
                .ApiLoadTestCompletedWithoutAbort(report);

            // Assert - Both successes and errors recorded
            Assert.True(report.Results.Phase3Api.SuccessCount > 0,
                "Should have some successful requests");
            Assert.True(report.Results.Phase3Api.ErrorCount > 0,
                "Should have some error requests from intermittent failures");

            // Assert - Error percentage is approximately 20% (allow 10-30% tolerance)
            var errorPercentage = 100.0 - report.Results.Phase3Api.SuccessPercentage;
            Assert.InRange(errorPercentage, 10.0, 30.0);

            Output.WriteLine($"API test with 3 workers and intermittent errors completed");
            Output.WriteLine($"Total requests: {report.Results.Phase3Api.TotalRequests}");
            Output.WriteLine($"Successes: {report.Results.Phase3Api.SuccessCount}");
            Output.WriteLine($"Errors: {report.Results.Phase3Api.ErrorCount}");
            Output.WriteLine($"Error percentage: {errorPercentage:F2}% (expected ~20%)");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 3.5: Verifies that various worker counts all complete successfully.
    /// Tests worker counts: 1, 3, 10 - each should complete with TotalRequests > 0.
    /// </summary>
    /// <param name="workerCount">Number of concurrent API workers</param>
    /// <param name="testPort">Port for the ConfigurableReferenceService</param>
    [Theory]
    [InlineData(1, 9983)]
    [InlineData(3, 9984)]
    [InlineData(10, 9985)]
    public async Task RunTestAsync_WithVariousWorkerCounts_ShouldAllComplete(int workerCount, int testPort)
    {
        // Arrange
        const int eventCount = 50;
        const int warmupEventCount = 5;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        var config = new TestConfigurationBuilder()
            .WithEventCount(eventCount)
            .WithWarmupEventCount(warmupEventCount)
            .WithApiWorkers(workerCount)
            .WithApiDuration(TimeSpan.FromSeconds(3)) // Short duration for test efficiency
            .WithServicePort(testPort)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
            await System.WaitForServiceHealthyAsync(port: testPort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert - Test completes successfully
            await Asserting.That(System.Orchestration.Orchestrator)
                .CompletedAllPhases(report);

            // Assert - TotalRequests > 0
            Assert.True(report.Results.Phase3Api.TotalRequests > 0,
                $"Expected TotalRequests > 0 with {workerCount} worker(s), got {report.Results.Phase3Api.TotalRequests}");

            Output.WriteLine($"API test with {workerCount} worker(s) completed successfully");
            Output.WriteLine($"Total requests: {report.Results.Phase3Api.TotalRequests}");
            Output.WriteLine($"Throughput: {report.Results.Phase3Api.ThroughputCallsPerSec:F2} req/sec");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
