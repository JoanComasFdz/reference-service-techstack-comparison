using JoanComasFdz.AssertingThat;
using Xunit;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

public static class DockerMonitoringAssertions
{
    public static AssertingThat<IDockerMonitor> HasCollectedMetrics(
        this AssertingThat<IDockerMonitor> assertingThat)
    {
        var monitor = assertingThat.InstanceToAssert;
        var metrics = monitor.GetCollectedMetrics();

        Assert.NotEmpty(metrics);

        return assertingThat;
    }

    public static AssertingThat<IDockerMonitor> HasNotCollectedMetrics(
        this AssertingThat<IDockerMonitor> assertingThat)
    {
        var monitor = assertingThat.InstanceToAssert;
        var metrics = monitor.GetCollectedMetrics();

        Assert.Empty(metrics);

        return assertingThat;
    }

    public static AssertingThat<IDockerMonitor> HasMinimumSampleCount(
        this AssertingThat<IDockerMonitor> assertingThat,
        int expectedMinimum)
    {
        var monitor = assertingThat.InstanceToAssert;
        var metrics = monitor.GetCollectedMetrics();

        Assert.True(metrics.Count >= expectedMinimum,
            $"{monitor.ContainerName} expected >= {expectedMinimum} samples, got {metrics.Count}");

        return assertingThat;
    }

    public static AssertingThat<IDockerMonitor> HasValidCpuPercentages(
        this AssertingThat<IDockerMonitor> assertingThat)
    {
        var monitor = assertingThat.InstanceToAssert;
        var metrics = monitor.GetCollectedMetrics();

        foreach (var metric in metrics)
        {
            Assert.True(metric.CpuPercent >= 0,
                $"{monitor.ContainerName} CPU% must be >= 0, got {metric.CpuPercent}");
            Assert.True(metric.CpuPercent <= 1000,
                $"{monitor.ContainerName} CPU% exceeds reasonable bound, got {metric.CpuPercent}");
        }

        return assertingThat;
    }

    public static AssertingThat<IDockerMonitor> HasValidMemoryMeasurements(
        this AssertingThat<IDockerMonitor> assertingThat)
    {
        var monitor = assertingThat.InstanceToAssert;
        var metrics = monitor.GetCollectedMetrics();

        foreach (var metric in metrics)
        {
            Assert.True(metric.MemoryMB > 0,
                $"{monitor.ContainerName} Memory must be > 0 MB, got {metric.MemoryMB}");
            Assert.True(metric.MemoryMB <= 100_000,
                $"{monitor.ContainerName} Memory exceeds reasonable bound, got {metric.MemoryMB} MB");
        }

        return assertingThat;
    }

    public static AssertingThat<IDockerMonitor> HasMetricsInChronologicalOrder(
        this AssertingThat<IDockerMonitor> assertingThat)
    {
        var monitor = assertingThat.InstanceToAssert;
        var metrics = monitor.GetCollectedMetrics();
        var timestamps = metrics.Select(m => m.Timestamp).ToList();

        Assert.Equal(timestamps.OrderBy(t => t).ToList(), timestamps);

        return assertingThat;
    }
}
