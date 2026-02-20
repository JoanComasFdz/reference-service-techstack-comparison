using JoanComasFdz.AssertingThat;
using PerformanceTester.DockerMonitoring.Monitoring;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

public static class DockerMonitoringAssertions
{
    public static AssertingThat<GetDockerMetricsDelegate> HasCollectedMetricsFor(
        this AssertingThat<GetDockerMetricsDelegate> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        Assert.NotEmpty(metrics);

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetricsDelegate> HasNotCollectedMetricsFor(
        this AssertingThat<GetDockerMetricsDelegate> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        Assert.Empty(metrics);

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetricsDelegate> HasMinimumSampleCountFor(
        this AssertingThat<GetDockerMetricsDelegate> assertingThat,
        NonEmptyString containerName,
        int expectedMinimum)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        Assert.True(
            metrics.Count >= expectedMinimum,
            $"{containerName} expected >= {expectedMinimum} samples, got {metrics.Count}");

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetricsDelegate> HasValidCpuPercentagesFor(
        this AssertingThat<GetDockerMetricsDelegate> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        foreach (var metric in metrics)
        {
            Assert.True(
                metric.CpuPercent >= 0,
                $"{containerName} CPU% must be >= 0, got {metric.CpuPercent}");
            Assert.True(
                metric.CpuPercent <= 1000,
                $"{containerName} CPU% exceeds reasonable bound, got {metric.CpuPercent}");
        }

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetricsDelegate> HasValidMemoryMeasurementsFor(
        this AssertingThat<GetDockerMetricsDelegate> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        foreach (var metric in metrics)
        {
            Assert.True(
                metric.MemoryMB > 0,
                $"{containerName} Memory must be > 0 MB, got {metric.MemoryMB}");
            Assert.True(
                metric.MemoryMB <= 100_000,
                $"{containerName} Memory exceeds reasonable bound, got {metric.MemoryMB} MB");
        }

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetricsDelegate> HasMetricsInChronologicalOrderFor(
        this AssertingThat<GetDockerMetricsDelegate> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);
        var timestamps = metrics.Select(m => m.Timestamp).ToList();

        Assert.Equal(timestamps.OrderBy(t => t).ToList(), timestamps);

        return assertingThat;
    }
}
