using JoanComasFdz.AssertingThat;
using Xunit;
using Xunit.Abstractions;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

namespace PerformanceTester.Orchestration.IntegrationTests;

/// <summary>
/// Collection definition to disable parallel execution for API abort tests.
/// Required because tests share the ConfigurableReferenceService HTTP listener.
/// </summary>
[CollectionDefinition("ApiAbortTests", DisableParallelization = true)]
public class ApiAbortTestsCollection { }

/// <summary>
/// Integration tests for API load test abort functionality.
/// Tests the abort behavior when consecutive API failures exceed threshold.
/// </summary>
/// <remarks>
/// These tests must run sequentially because they share ConfigurableReferenceService resources.
/// The [Collection] attribute ensures xUnit runs them one at a time.
/// </remarks>
[Collection("ApiAbortTests")]
public sealed class OrchestratorApiAbortTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Verifies that when API server returns errors consistently, the load test aborts
    /// and reports the abort reason in the test report.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenApiServerReturnsErrors_ShouldAbortAndReportReason()
    {
        // Arrange
        System.DotNetAotService.Stop();

        System.ConfigurableReferenceService.ConfigureHttpResponse(statusCode: 500);
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 1,
            warmupEventCount: 1);

        var config = new TestConfigurationBuilder()
            .WithEventCount(1)
            .WithWarmupEventCount(1)
            .WithWarmupApiCallCount(3)
            .WithApiDuration(TimeSpan.FromSeconds(10))
            .WithMaxConsecutiveApiFailures(3)
            .WithServicePort(9991)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: config.ServicePort);
            await System.WaitForServiceHealthyAsync(port: config.ServicePort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert
            await Asserting.That(System.Orchestration.Orchestrator)
                .ApiLoadTestWasAborted(report, "consecutive failures");

            Assert.True(report.Results.Phase3Api.ErrorCount > 0,
                "Should have recorded errors");
            Assert.True(report.Results.Phase3Api.DurationSeconds < config.ApiDurationOrDefault.TotalSeconds,
                "Should have terminated early");

            Output.WriteLine($"API test aborted as expected after {report.Results.Phase3Api.DurationSeconds:F2}s");
            Output.WriteLine($"Abort reason: {report.Results.Phase3Api.AbortReason}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Verifies that when MaxConsecutiveApiFailures is set to 0 (disabled),
    /// the API load test runs for the full configured duration regardless of failures.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenAbortDisabled_ShouldCompleteFullDuration()
    {
        // Arrange
        System.DotNetAotService.Stop();

        System.ConfigurableReferenceService.ConfigureHttpResponse(statusCode: 500);
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 1,
            warmupEventCount: 1);

        var apiDuration = TimeSpan.FromSeconds(5);
        var config = new TestConfigurationBuilder()
            .WithEventCount(1)
            .WithWarmupEventCount(1)
            .WithWarmupApiCallCount(3)
            .WithApiDuration(apiDuration)
            .WithMaxConsecutiveApiFailures(0) // Disabled
            .WithServicePort(9992)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: config.ServicePort);
            await System.WaitForServiceHealthyAsync(port: config.ServicePort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert
            await Asserting.That(System.Orchestration.Orchestrator)
                .ApiLoadTestCompletedWithoutAbort(report);

            await Asserting.That(System.Orchestration.Orchestrator)
                .ApiLoadTestDurationApproximately(report, apiDuration, tolerance: TimeSpan.FromSeconds(2));

            Output.WriteLine($"API test completed full duration: {report.Results.Phase3Api.DurationSeconds:F2}s");
            Output.WriteLine($"Errors recorded: {report.Results.Phase3Api.ErrorCount}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Verifies that with a higher abort threshold, intermittent failures
    /// (non-consecutive) don't trigger an abort.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WithHigherAbortThreshold_ShouldTolerateIntermittentFailures()
    {
        // Arrange
        System.DotNetAotService.Stop();

        System.ConfigurableReferenceService.ConfigureIntermittentFailures(failEveryNthRequest: 3);
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 1,
            warmupEventCount: 1);

        var apiDuration = TimeSpan.FromSeconds(10);
        var config = new TestConfigurationBuilder()
            .WithEventCount(1)
            .WithWarmupEventCount(1)
            .WithWarmupApiCallCount(3)
            .WithApiDuration(apiDuration)
            .WithMaxConsecutiveApiFailures(10) // High tolerance
            .WithServicePort(9993)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: config.ServicePort);
            await System.WaitForServiceHealthyAsync(port: config.ServicePort, timeout: TimeSpan.FromSeconds(10));

            // Act
            var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

            // Assert
            await Asserting.That(System.Orchestration.Orchestrator)
                .ApiLoadTestCompletedWithoutAbort(report);

            Assert.True(report.Results.Phase3Api.ErrorCount > 0,
                "Should have some errors from intermittent failures");
            Assert.True(report.Results.Phase3Api.SuccessCount > 0,
                "Should have some successes between failures");

            Output.WriteLine($"API test completed without abort despite {report.Results.Phase3Api.ErrorCount} errors");
            Output.WriteLine($"Success count: {report.Results.Phase3Api.SuccessCount}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
