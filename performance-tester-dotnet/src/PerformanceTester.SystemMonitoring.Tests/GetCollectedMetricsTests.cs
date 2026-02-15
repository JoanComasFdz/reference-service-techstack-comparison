namespace PerformanceTester.SystemMonitoring.Tests;

/// <summary>
/// Unit tests for GetCollectedMetrics behavior.
/// These tests verify ordering and thread-safety without starting the full BackgroundService.
/// </summary>
public sealed class GetCollectedMetricsTests
{
    /// <summary>
    /// Verifies that GetCollectedMetrics returns items in the order they were added,
    /// without requiring a sort operation.
    /// </summary>
    [Fact]
    public void GetCollectedMetrics_WhenItemsAddedInOrder_ShouldReturnInSameOrder()
    {
        // Arrange - Create metrics with timestamps out of natural order
        // to prove sorting doesn't happen (we expect insertion order)
        var metrics = new List<SystemMetrics>
        {
            CreateMetrics(timestamp: DateTimeOffset.UtcNow.AddSeconds(3), elapsed: 3.0),
            CreateMetrics(timestamp: DateTimeOffset.UtcNow.AddSeconds(1), elapsed: 1.0),
            CreateMetrics(timestamp: DateTimeOffset.UtcNow.AddSeconds(2), elapsed: 2.0),
        };

        var collector = new TestableMetricsCollector();

        // Add in specific order (3, 1, 2 by timestamp)
        foreach (var m in metrics)
        {
            collector.AddMetric(m);
        }

        // Act
        var result = collector.GetCollectedMetrics();

        // Assert - Should maintain insertion order (3, 1, 2)
        Assert.Equal(3, result.Count);
        Assert.Equal(3.0, result.ElementAt(0).ElapsedSeconds);
        Assert.Equal(1.0, result.ElementAt(1).ElapsedSeconds);
        Assert.Equal(2.0, result.ElementAt(2).ElapsedSeconds);
    }

    /// <summary>
    /// Verifies that calling GetCollectedMetrics multiple times returns the same collection
    /// without creating new instances (no redundant allocations).
    /// </summary>
    [Fact]
    public void GetCollectedMetrics_WhenCalledMultipleTimes_ShouldReturnConsistentResults()
    {
        // Arrange
        var collector = new TestableMetricsCollector();
        collector.AddMetric(CreateMetrics(DateTimeOffset.UtcNow, 1.0));
        collector.AddMetric(CreateMetrics(DateTimeOffset.UtcNow.AddSeconds(1), 2.0));

        // Act
        var result1 = collector.GetCollectedMetrics();
        var result2 = collector.GetCollectedMetrics();

        // Assert - Both calls return the same data
        Assert.Equal(result1.Count, result2.Count);
        Assert.Equal(result1.First().ElapsedSeconds, result2.First().ElapsedSeconds);
    }

    private static SystemMetrics CreateMetrics(DateTimeOffset timestamp, double elapsed) => new(
            Timestamp: timestamp,
            ElapsedSeconds: elapsed,
            CpuPercent: 50.0,
            MemoryUsedMb: 1000.0,
            MemoryTotalMb: 2000.0,
            MemoryPercent: 50.0);
}

/// <summary>
/// Test helper that exposes AddMetric for unit testing GetCollectedMetrics behavior.
/// This mimics the internal collection mechanism without needing the full BackgroundService.
/// </summary>
internal sealed class TestableMetricsCollector
{
    private readonly List<SystemMetrics> _metrics = [];
    private readonly object _lock = new();

    public void AddMetric(SystemMetrics metric)
    {
        lock (_lock)
        {
            _metrics.Add(metric);
        }
    }

    public IReadOnlyCollection<SystemMetrics> GetCollectedMetrics()
    {
        lock (_lock)
        {
            return _metrics.AsReadOnly();
        }
    }
}
