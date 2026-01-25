using JoanComasFdz.AssertingThat;

namespace PerformanceTester.ApiLoadTesting.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for ApiLoadTesting integration tests.
/// Makes tests more readable by expressing assertions in domain language.
/// Uses AssertingThat pattern for fluent chaining with xUnit Assert methods internally.
/// IMPORTANT: All assertion methods return AssertingThat&lt;T&gt; to enable fluent chaining.
/// </summary>
public static class ApiLoadTestingAssertions
{
    /// <summary>
    /// Asserts that all metrics have non-negative values (>= 0).
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> AllMetricsHaveNonNegativeValues(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;

        Assert.True(result.TotalRequests > 0, "Total requests must be positive");
        Assert.True(result.TotalDuration > TimeSpan.Zero, "Duration must be positive");
        Assert.True(result.AverageRequestDurationMs >= 0, "Average duration cannot be negative");
        Assert.True(result.P95RequestDurationMs >= 0, "P95 duration cannot be negative");
        Assert.True(result.P99RequestDurationMs >= 0, "P99 duration cannot be negative");
        Assert.True(result.RequestsPerSecond >= 0, "Requests per second cannot be negative");

        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test result has no failed requests.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> HasNoFailedRequests(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.Equal(0, result.FailedRequests);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test result has at least the specified number of requests.
    /// </summary>
    /// <param name="assertingThat">The asserting instance.</param>
    /// <param name="minimumRequests">Minimum expected request count.</param>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> HasAtLeastRequests(
        this AssertingThat<ApiLoadTestResult> assertingThat,
        int minimumRequests)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.True(
            result.TotalRequests >= minimumRequests,
            $"Expected at least {minimumRequests} requests, but found {result.TotalRequests}");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test result has throughput samples.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> HasThroughputSamples(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.NotNull(result.ThroughputSamples);
        Assert.NotEmpty(result.ThroughputSamples);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test was aborted due to consecutive failures.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> WasAborted(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.True(result.WasAborted, "Expected test to be aborted, but it was not");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test was not aborted.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> WasNotAborted(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.False(result.WasAborted, $"Expected test to not be aborted, but it was aborted with reason: {result.AbortReason}");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test has an abort reason containing the specified text.
    /// </summary>
    /// <param name="assertingThat">The asserting instance.</param>
    /// <param name="expectedText">Text expected to be in the abort reason.</param>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> HasAbortReasonContaining(
        this AssertingThat<ApiLoadTestResult> assertingThat,
        string expectedText)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.NotNull(result.AbortReason);
        Assert.Contains(expectedText, result.AbortReason, StringComparison.OrdinalIgnoreCase);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the test result has failed requests.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> HasFailedRequests(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;
        Assert.True(result.FailedRequests > 0, "Expected at least one failed request, but found none");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that throughput samples have been calculated (not all zeros).
    /// This catches the bug where RPS is hardcoded to 0 instead of being calculated.
    /// Note: Some individual samples may legitimately be 0 if no requests completed
    /// in that interval, so we check that at least some samples have non-zero RPS.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> HasCalculatedThroughputRates(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;

        Assert.NotNull(result.ThroughputSamples);
        Assert.NotEmpty(result.ThroughputSamples);

        // At least some samples should have non-zero RPS if requests were processed
        var nonZeroSamples = result.ThroughputSamples.Count(s => s.RequestsPerSecond > 0);

        Assert.True(nonZeroSamples > 0,
            $"Expected at least some throughput samples to have calculated RPS > 0, " +
            $"but all {result.ThroughputSamples.Count} samples have RPS = 0. " +
            $"Total requests: {result.TotalRequests}, Duration: {result.TotalDuration.TotalSeconds}s. " +
            $"This indicates RPS was never calculated (bug: hardcoded to 0).");

        return assertingThat;
    }

    /// <summary>
    /// Asserts that throughput samples have reasonable RPS values relative to the overall average.
    /// Validates that the sample-level RPS values are consistent with the overall test RPS.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ApiLoadTestResult> ThroughputSamplesAreConsistentWithOverallRPS(
        this AssertingThat<ApiLoadTestResult> assertingThat)
    {
        var result = assertingThat.InstanceToAssert;

        Assert.NotNull(result.ThroughputSamples);

        // First verify we have meaningful data to compare
        Assert.True(result.RequestsPerSecond > 0,
            $"Overall RequestsPerSecond must be positive to validate sample consistency, " +
            $"but got {result.RequestsPerSecond}");

        if (result.ThroughputSamples.Count == 0)
            return assertingThat;

        var avgSampleRps = result.ThroughputSamples.Average(s => s.RequestsPerSecond);

        // Sample average should be within 50% of overall average (allowing for variance)
        var lowerBound = result.RequestsPerSecond * 0.5;
        var upperBound = result.RequestsPerSecond * 1.5;

        Assert.True(avgSampleRps >= lowerBound && avgSampleRps <= upperBound,
            $"Average sample RPS ({avgSampleRps:F2}) should be within 50% of overall RPS ({result.RequestsPerSecond:F2}). " +
            $"Expected range: [{lowerBound:F2}, {upperBound:F2}]");

        return assertingThat;
    }
}
