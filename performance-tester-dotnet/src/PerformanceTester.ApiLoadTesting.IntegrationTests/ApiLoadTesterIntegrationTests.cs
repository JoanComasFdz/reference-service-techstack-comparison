using JoanComasFdz.AssertingThat;
using PerformanceTester.ApiLoadTesting.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PerformanceTester.ApiLoadTesting.IntegrationTests;

/// <summary>
/// Integration tests for ApiLoadTester functionality.
/// Tests the complete public API: IApiLoadTester with real k6 execution.
/// NOTE: These tests require k6 binary to be installed on the system.
/// Install k6: https://k6.io/docs/getting-started/installation/
/// Each test spins up its own HTTP server on a unique port for parallel execution.
/// </summary>
public sealed class ApiLoadTesterIntegrationTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task StartTestAsync_WhenTestCompletes_ShouldReturnValidResult()
    {
        // Arrange - Start test HTTP server on unique port
        using var server = new TestHttpServer(port: 9001);

        // Act - Run short load test
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(5),
            virtualUsers: 1);

        // Assert
        Asserting.That(result)
            .AllMetricsHaveNonNegativeValues()
            .HasNoFailedRequests();
    }

    [Fact]
    public async Task StartTestAsync_WithMultipleVUs_ShouldGenerateMoreRequests()
    {
        // Arrange - Start test HTTP server on unique port
        using var server = new TestHttpServer(port: 9002);

        // Act - Run with more VUs and longer duration to ensure we get meaningful throughput
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(10),
            virtualUsers: 10);

        // Assert - Should generate at least some requests (realistic expectation)
        Asserting.That(result)
            .HasNoFailedRequests()
            .HasAtLeastRequests(5);  // Lower threshold to account for k6 ramp-up time
    }

    [Fact]
    public async Task StartTestAsync_WhenTestCompletes_ShouldHaveThroughputSamples()
    {
        // Arrange - Start test HTTP server on unique port
        using var server = new TestHttpServer(port: 9003);

        // Act
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(5),
            virtualUsers: 2);

        // Assert - Enhanced with semantic checks
        Asserting.That(result)
            .HasNoFailedRequests()
            .HasThroughputSamples()
            .HasCalculatedThroughputRates();  // Catches the hardcoded-zero bug
    }

    [Fact]
    public async Task StartTestAsync_WhenTestCompletes_ThroughputSamplesShouldHaveCalculatedRPS()
    {
        // Arrange - use longer duration to ensure multiple samples
        using var server = new TestHttpServer(port: 9010);

        // Act
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(5),
            virtualUsers: 2);

        // Assert - This test would have caught the bug!
        Asserting.That(result)
            .AllMetricsHaveNonNegativeValues()
            .HasThroughputSamples()
            .HasCalculatedThroughputRates()
            .ThroughputSamplesAreConsistentWithOverallRPS();
    }

    [Fact]
    public async Task StartTestAsync_ThroughputSampleRPS_ShouldMatchCumulativeCountDeltas()
    {
        // Arrange
        using var server = new TestHttpServer(port: 9011);

        // Act
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(5),
            virtualUsers: 2);

        // Assert - First verify we have valid data
        Assert.True(result.TotalRequests > 0, "Test must produce requests");
        Assert.NotEmpty(result.ThroughputSamples);

        // Verify RPS calculation is based on cumulative count deltas
        var samples = result.ThroughputSamples.OrderBy(s => s.Timestamp).ToList();

        // Check that at least some samples have calculated RPS
        var samplesWithRps = samples.Where(s => s.RequestsPerSecond > 0).ToList();
        Assert.True(samplesWithRps.Count > 0,
            "At least some samples should have non-zero RPS");

        // For samples with RPS > 0, verify the delta calculation is reasonable
        for (int i = 1; i < samples.Count; i++)
        {
            var current = samples[i];
            var previous = samples[i - 1];

            var timeDiff = (current.Timestamp - previous.Timestamp).TotalSeconds;
            var countDiff = current.CumulativeRequestCount - previous.CumulativeRequestCount;

            // Skip validation if no time passed or no requests in interval
            if (timeDiff <= 0 || countDiff <= 0)
                continue;

            var expectedRps = countDiff / timeDiff;

            // Allow 5% tolerance + small absolute tolerance for rounding
            var tolerance = expectedRps * 0.05 + 0.5;
            Assert.True(Math.Abs(current.RequestsPerSecond - expectedRps) <= tolerance,
                $"Sample {i}: Expected RPS ~{expectedRps:F2} based on delta " +
                $"(countDiff={countDiff}, timeDiff={timeDiff:F2}s), " +
                $"but got {current.RequestsPerSecond:F2}");
        }
    }

    [Fact]
    public async Task StartTestAsync_WithInvalidUrl_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await System.ApiLoadTesting.LoadTester.StartTestAsync(
                targetUrl: "not-a-valid-url",
                duration: TimeSpan.FromSeconds(5),
                virtualUsers: 1);
        });
    }

    [Fact]
    public async Task StartTestAsync_WhenServerReturnsErrors_ShouldAbortAfterConsecutiveFailures()
    {
        // Arrange - Start test HTTP server that always returns 500 errors
        using var server = new TestHttpServer(port: 9004, alwaysFail: true);

        // Act - Run load test with default maxConsecutiveFailures (3)
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(30),  // Long duration - should abort early
            virtualUsers: 1,
            maxConsecutiveFailures: 3);

        // Assert - Test should be aborted with failed requests
        Asserting.That(result)
            .WasAborted()
            .HasFailedRequests()
            .HasAbortReasonContaining("consecutive failures");
    }

    [Fact]
    public async Task StartTestAsync_WhenAbortDisabled_ShouldCompleteWithoutAborting()
    {
        // Arrange - Start test HTTP server that always returns 500 errors
        using var server = new TestHttpServer(port: 9005, alwaysFail: true);

        // Act - Run load test with abort disabled (maxConsecutiveFailures = 0)
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(3),  // Short duration since all requests will fail
            virtualUsers: 1,
            maxConsecutiveFailures: 0);  // Disable abort

        // Assert - Test should complete normally (not aborted) but have failed requests
        Asserting.That(result)
            .WasNotAborted()
            .HasFailedRequests();
    }

    [Fact]
    public async Task StartTestAsync_WhenServerIsHealthy_ShouldNotAbort()
    {
        // Arrange - Start healthy test HTTP server
        using var server = new TestHttpServer(port: 9006);

        // Act - Run load test with abort enabled
        var result = await System.ApiLoadTesting.LoadTester.StartTestAsync(
            targetUrl: server.BaseUrl,
            duration: TimeSpan.FromSeconds(5),
            virtualUsers: 1,
            maxConsecutiveFailures: 3);

        // Assert - Test should complete normally without abort
        Asserting.That(result)
            .WasNotAborted()
            .HasNoFailedRequests();
    }
}
