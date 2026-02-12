using JoanComasFdz.AssertingThat;
using JoanComasFdz.Result;
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
    /// Asserts that service discovery returned a setup failure.
    /// </summary>
    public static async Task ReturnsSetupFailureWhenServiceNotFound(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestConfiguration config)
    {
        var result = await assertingThat.InstanceToAssert.RunTestAsync(config);

        Assert.True(result.IsFailure, "Expected a failure result when service is not found");
        Assert.Equal(TestPhase.Setup, result.FailureError.Phase);
        Assert.Contains("Service not found on port", result.FailureError.Message);
        Assert.Contains(config.ServicePort.ToString(), result.FailureError.Message);
    }

    /// <summary>
    /// Asserts that the orchestrator returns an event test failure due to consumer inactivity timeout.
    /// Verifies the failure message includes the expected progress (received count).
    /// </summary>
    /// <param name="assertingThat">The asserting wrapper</param>
    /// <param name="config">Test configuration</param>
    /// <param name="expectedReceivedCount">Expected number of events received before timeout</param>
    public static async Task ReturnsEventTestFailureForInactivityTimeout(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestConfiguration config,
        int expectedReceivedCount)
    {
        var result = await assertingThat.InstanceToAssert.RunTestAsync(config);

        Assert.True(result.IsFailure, "Expected a failure result for inactivity timeout");
        Assert.Equal(TestPhase.EventTest, result.FailureError.Phase);
        Assert.Contains("Inactivity timeout", result.FailureError.Message);
        Assert.Contains($"{expectedReceivedCount}/{config.EventCount}", result.FailureError.Message);
    }

    /// <summary>
    /// Asserts that the API load test was aborted due to consecutive failures.
    /// </summary>
    /// <param name="assertingThat">The asserting wrapper</param>
    /// <param name="report">Test report to validate</param>
    /// <param name="expectedReasonContains">Expected substring in abort reason</param>
    public static Task ApiLoadTestWasAborted(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestReport report,
        string expectedReasonContains)
    {
        Assert.True(report.Results.Phase3Api.WasAborted,
            "API load test should have been aborted");
        Assert.NotNull(report.Results.Phase3Api.AbortReason);
        Assert.Contains(expectedReasonContains, report.Results.Phase3Api.AbortReason!);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that the API load test completed without abort.
    /// </summary>
    /// <param name="assertingThat">The asserting wrapper</param>
    /// <param name="report">Test report to validate</param>
    public static Task ApiLoadTestCompletedWithoutAbort(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestReport report)
    {
        Assert.False(report.Results.Phase3Api.WasAborted,
            "API load test should not have been aborted");
        Assert.Null(report.Results.Phase3Api.AbortReason);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that the API load test duration is approximately as expected.
    /// </summary>
    /// <param name="assertingThat">The asserting wrapper</param>
    /// <param name="report">Test report to validate</param>
    /// <param name="expectedDuration">Expected duration</param>
    /// <param name="tolerance">Acceptable tolerance range</param>
    public static Task ApiLoadTestDurationApproximately(
        this AssertingThat<ITestOrchestrator> assertingThat,
        TestReport report,
        TimeSpan expectedDuration,
        TimeSpan tolerance)
    {
        var actualDuration = TimeSpan.FromSeconds(report.Results.Phase3Api.DurationSeconds);
        var minDuration = expectedDuration - tolerance;
        var maxDuration = expectedDuration + tolerance;

        Assert.InRange(actualDuration, minDuration, maxDuration);
        return Task.CompletedTask;
    }
}
