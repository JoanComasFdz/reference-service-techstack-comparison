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
        this AssertingThat<TestReport> assertingThat)
    {
        var report = assertingThat.InstanceToAssert;

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
    /// Asserts that the API load test was aborted due to consecutive failures.
    /// </summary>
    public static Task ApiLoadTestWasAborted(
        this AssertingThat<TestReport> assertingThat,
        string expectedReasonContains)
    {
        var report = assertingThat.InstanceToAssert;

        Assert.True(report.Results.Phase3Api.WasAborted,
            "API load test should have been aborted");
        Assert.NotNull(report.Results.Phase3Api.AbortReason);
        Assert.Contains(expectedReasonContains, report.Results.Phase3Api.AbortReason!);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that the API load test completed without abort.
    /// </summary>
    public static Task ApiLoadTestCompletedWithoutAbort(
        this AssertingThat<TestReport> assertingThat)
    {
        var report = assertingThat.InstanceToAssert;

        Assert.False(report.Results.Phase3Api.WasAborted,
            "API load test should not have been aborted");
        Assert.Null(report.Results.Phase3Api.AbortReason);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that the API load test duration is approximately as expected.
    /// </summary>
    public static Task ApiLoadTestDurationApproximately(
        this AssertingThat<TestReport> assertingThat,
        TimeSpan expectedDuration,
        TimeSpan tolerance)
    {
        var report = assertingThat.InstanceToAssert;

        var actualDuration = TimeSpan.FromSeconds(report.Results.Phase3Api.DurationSeconds);
        var minDuration = expectedDuration - tolerance;
        var maxDuration = expectedDuration + tolerance;

        Assert.InRange(actualDuration, minDuration, maxDuration);
        return Task.CompletedTask;
    }
}
