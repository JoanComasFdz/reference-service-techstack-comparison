using JoanComasFdz.AssertingThat;
using Xunit;

namespace PerformanceTester.SystemMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for SystemMonitoring integration tests.
/// </summary>
public static class SystemMonitoringAssertions
{
    /// <summary>
    /// Asserts that system metrics were collected.
    /// </summary>
    public static AssertingThat<ISystemMonitor> HasCollectedMetrics(
        this AssertingThat<ISystemMonitor> assertingThat)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        Assert.NotNull(metrics);
        Assert.NotEmpty(metrics);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that at least the specified number of metrics were collected.
    /// </summary>
    public static AssertingThat<ISystemMonitor> HasAtLeastMetrics(
        this AssertingThat<ISystemMonitor> assertingThat,
        int minimumCount)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        Assert.True(
            metrics.Count >= minimumCount,
            $"Expected at least {minimumCount} metrics, but found {metrics.Count}");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that all metrics have valid data (non-negative values, valid memory).
    /// </summary>
    public static AssertingThat<ISystemMonitor> HasValidMetrics(
        this AssertingThat<ISystemMonitor> assertingThat)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        foreach (var metric in metrics)
        {
            // CPU can be 0 but not negative
            Assert.True(metric.CpuPercent >= 0,
                $"CPU percent cannot be negative: {metric.CpuPercent}");
            Assert.True(metric.CpuPercent <= 100,
                $"CPU percent cannot exceed 100: {metric.CpuPercent}");

            // Memory must be positive and logical
            Assert.True(metric.MemoryTotalMb > 0,
                $"Total memory must be positive: {metric.MemoryTotalMb}");
            Assert.True(metric.MemoryUsedMb >= 0,
                $"Used memory cannot be negative: {metric.MemoryUsedMb}");
            Assert.True(metric.MemoryUsedMb <= metric.MemoryTotalMb,
                $"Used memory ({metric.MemoryUsedMb}) cannot exceed total ({metric.MemoryTotalMb})");
            Assert.True(metric.MemoryPercent >= 0 && metric.MemoryPercent <= 100,
                $"Memory percent must be 0-100: {metric.MemoryPercent}");

            // Elapsed seconds must increase
            Assert.True(metric.ElapsedSeconds >= 0,
                $"Elapsed seconds cannot be negative: {metric.ElapsedSeconds}");
        }
        return assertingThat;
    }

    /// <summary>
    /// Asserts that metrics have chronological timestamps.
    /// </summary>
    public static AssertingThat<ISystemMonitor> HasChronologicalTimestamps(
        this AssertingThat<ISystemMonitor> assertingThat)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        var ordered = metrics.OrderBy(m => m.Timestamp).ToList();

        for (int i = 0; i < ordered.Count - 1; i++)
        {
            Assert.True(
                ordered[i].Timestamp <= ordered[i + 1].Timestamp,
                $"Timestamps not in chronological order at index {i}");
        }
        return assertingThat;
    }

    /// <summary>
    /// Asserts that CpuCount is positive.
    /// </summary>
    public static AssertingThat<ISystemMonitor> HasValidCpuCount(
        this AssertingThat<ISystemMonitor> assertingThat)
    {
        var cpuCount = assertingThat.InstanceToAssert.CpuCount;
        Assert.True(cpuCount > 0, $"CPU count must be positive: {cpuCount}");
        return assertingThat;
    }
}
