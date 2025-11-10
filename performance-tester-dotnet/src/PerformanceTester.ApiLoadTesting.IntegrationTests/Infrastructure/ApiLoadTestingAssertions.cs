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
}
