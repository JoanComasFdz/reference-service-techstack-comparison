using JoanComasFdz.AssertingThat;
using PerformanceTester.Reporting;
using Xunit;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

public static class OrchestrationAssertions
{
    /// <summary>
    /// Asserts that all phases completed successfully.
    /// </summary>
    public static Task CompletedAllPhases(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestReport report)
    {
        // Verify event test phases
        Assert.True(report.Results.Phase1Publish.DurationSeconds > 0,
            "Event publish phase should have duration");
        Assert.True(report.Results.Phase2Consume.DurationSeconds > 0,
            "Event consume phase should have duration");
        Assert.NotEmpty(report.EventsThroughputSamples);

        // Verify API test phase
        Assert.True(report.Results.Phase3Api.DurationSeconds > 0,
            "API test phase should have duration");
        Assert.True(report.Results.Phase3Api.TotalRequests > 0);

        // Verify reporting phase
        Assert.NotNull(report.System);
        Assert.NotNull(report.MonitoredProcess);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that service discovery timeout occurred.
    /// </summary>
    public static async Task ThrowsTimeoutExceptionWhenServiceNotFound(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestConfiguration config)
    {
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await assertingThat.InstanceToAssert.RunTestAsync(config);
        });

        Assert.Contains("Service not found on port", exception.Message);
        Assert.Contains(config.ServicePort.ToString(), exception.Message);
    }

    /// <summary>
    /// Asserts that the orchestrator throws TimeoutException due to consumer inactivity timeout.
    /// Verifies the exception message includes the expected progress (received count).
    /// </summary>
    /// <param name="assertingThat">The asserting wrapper</param>
    /// <param name="config">Test configuration</param>
    /// <param name="expectedReceivedCount">Expected number of events received before timeout</param>
    public static async Task ThrowsConsumerInactivityTimeout(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestConfiguration config,
        int expectedReceivedCount)
    {
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await assertingThat.InstanceToAssert.RunTestAsync(config);
        });

        // Verify exception message includes timeout reason and progress
        Assert.Contains("Inactivity timeout", exception.Message);
        Assert.Contains($"{expectedReceivedCount}/{config.EventCount}", exception.Message);
    }
}
