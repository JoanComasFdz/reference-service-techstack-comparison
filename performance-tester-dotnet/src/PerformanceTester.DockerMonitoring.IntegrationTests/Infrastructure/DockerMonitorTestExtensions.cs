namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Test extensions for IDockerMonitor.
/// Provides convenient waiting mechanisms without polluting the phase enum.
/// </summary>
public static class DockerMonitorTestExtensions
{
    /// <summary>
    /// Waits until the monitor has collected at least the specified number of samples.
    /// </summary>
    /// <param name="monitor">The monitor to wait on.</param>
    /// <param name="minimumCount">Minimum number of samples to wait for.</param>
    /// <param name="timeout">Timeout (default: 30 seconds).</param>
    /// <exception cref="TimeoutException">Thrown if timeout expires before reaching sample count.</exception>
    public static async Task WaitForSampleCountAsync(
        this IDockerMonitor monitor,
        int minimumCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        try
        {
            while (monitor.GetCollectedMetrics().Count < minimumCount)
            {
                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            var actualCount = monitor.GetCollectedMetrics().Count;
            throw new TimeoutException(
                $"Timeout waiting for {minimumCount} samples from '{monitor.ContainerName}'. " +
                $"Only received {actualCount} samples after {effectiveTimeout.TotalSeconds}s.");
        }
    }
}
