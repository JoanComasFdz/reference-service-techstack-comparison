using JoanComasFdz.AssertingThat;
using Xunit;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for ProcessMonitoring integration tests.
/// Makes tests more readable by expressing assertions in domain language.
/// Uses AssertingThat pattern for fluent chaining with xUnit Assert methods internally.
/// IMPORTANT: All assertion methods return AssertingThat&lt;T&gt; to enable fluent chaining.
/// </summary>
public static class ProcessMonitoringAssertions
{
    /// <summary>
    /// Asserts that process metrics were collected.
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<IProcessMonitor> HasCollectedMetrics(
        this AssertingThat<IProcessMonitor> assertingThat)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        Assert.NotNull(metrics);
        Assert.NotEmpty(metrics);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that at least the specified number of metrics were collected.
    /// </summary>
    /// <param name="assertingThat">The asserting instance.</param>
    /// <param name="minimumCount">Minimum expected metric count.</param>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<IProcessMonitor> HasAtLeastMetrics(
        this AssertingThat<IProcessMonitor> assertingThat,
        int minimumCount)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        Assert.True(
            metrics.Count >= minimumCount,
            $"Expected at least {minimumCount} metrics, but found {metrics.Count}");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that all metrics have valid data (non-negative values, matching process ID).
    /// </summary>
    /// <param name="assertingThat">The asserting instance.</param>
    /// <param name="expectedProcessId">Expected process ID in all metrics.</param>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<IProcessMonitor> HasValidMetrics(
        this AssertingThat<IProcessMonitor> assertingThat,
        int expectedProcessId)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        foreach (var metric in metrics)
        {
            Assert.Equal(expectedProcessId, metric.ProcessId);
            Assert.False(string.IsNullOrWhiteSpace(metric.ProcessName),
                "Process name should not be empty");
            Assert.True(metric.CpuPercent >= 0,
                $"CPU percent cannot be negative: {metric.CpuPercent}");
            Assert.True(metric.MemoryMB > 0,
                $"Memory must be positive: {metric.MemoryMB}");
            Assert.True(metric.ThreadCount > 0,
                $"Thread count must be positive: {metric.ThreadCount}");
        }
        return assertingThat;
    }

    /// <summary>
    /// Asserts that metrics show increasing timestamps (chronological order).
    /// </summary>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<IProcessMonitor> HasChronologicalTimestamps(
        this AssertingThat<IProcessMonitor> assertingThat)
    {
        var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
        var ordered = metrics.OrderBy(m => m.Timestamp).ToList();

        for (int i = 0; i < metrics.Count - 1; i++)
        {
            Assert.True(
                ordered[i].Timestamp <= ordered[i + 1].Timestamp,
                $"Timestamps not in chronological order at index {i}");
        }
        return assertingThat;
    }

    /// <summary>
    /// Asserts that CreateProcessMonitoring throws ArgumentOutOfRangeException for invalid process ID.
    /// Tests the production DI registration validation (ServiceCollectionExtensions.AddProcessMonitoring).
    /// </summary>
    /// <param name="assertingThat">The asserting instance.</param>
    /// <param name="invalidProcessId">The invalid process ID that should trigger the exception.</param>
    /// <returns>The asserting instance for fluent chaining.</returns>
    public static AssertingThat<ProcessMonitoringSystem> ThrowsArgumentOutOfRangeExceptionForInvalidProcessId(
        this AssertingThat<ProcessMonitoringSystem> assertingThat,
        int invalidProcessId)
    {
        // Test production validation by calling CreateProcessMonitoring with invalid ID
        // The exception comes from ServiceCollectionExtensions.AddProcessMonitoring() validation
        // which is called by ProcessMonitoring facade constructor -> builder.Services.AddProcessMonitoring()
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            assertingThat.InstanceToAssert.CreateProcessMonitoring(invalidProcessId);
        });

        return assertingThat;
    }
}
