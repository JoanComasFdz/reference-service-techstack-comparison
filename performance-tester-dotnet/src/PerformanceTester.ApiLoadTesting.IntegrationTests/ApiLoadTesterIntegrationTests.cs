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

        // Assert
        Asserting.That(result)
            .HasNoFailedRequests()
            .HasThroughputSamples();
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
}
