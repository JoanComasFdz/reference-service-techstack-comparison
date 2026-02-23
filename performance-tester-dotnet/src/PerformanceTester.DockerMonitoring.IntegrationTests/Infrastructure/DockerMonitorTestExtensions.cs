using PerformanceTester.DockerMonitoring;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Test extensions for GetDockerMetrics delegate.
/// Provides convenient waiting mechanisms for sample collection.
/// </summary>
public static class DockerMonitorTestExtensions
{
    /// <summary>
    /// Waits until the specified container has collected at least the given number of samples.
    /// </summary>
    /// <param name="getDockerMetrics">The metrics retrieval delegate.</param>
    /// <param name="containerName">Container to check.</param>
    /// <param name="minimumCount">Minimum number of samples to wait for.</param>
    /// <param name="timeout">Timeout (default: 30 seconds).</param>
    /// <exception cref="TimeoutException">Thrown if timeout expires before reaching sample count.</exception>
    public static async Task WaitForSampleCountAsync(
        this GetDockerMetricsDelegate getDockerMetrics,
        NonEmptyString containerName,
        int minimumCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        try
        {
            while (getDockerMetrics(containerName).Count < minimumCount)
            {
                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            var actualCount = getDockerMetrics(containerName).Count;
            throw new TimeoutException(
                $"Timeout waiting for {minimumCount} samples from '{containerName}'. " +
                $"Only received {actualCount} samples after {effectiveTimeout.TotalSeconds}s.");
        }
    }
}
