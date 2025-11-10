using JoanComasFdz.AssertingThat;
using PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests;

/// <summary>
/// Integration tests for ProcessMonitor functionality.
/// Tests the complete public API: IProcessMonitor with real process monitoring.
/// Monitors the current test process (self-monitoring pattern).
/// </summary>
public sealed class ProcessMonitorIntegrationTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoringCurrentProcess_ShouldCollectMetrics()
    {
        // Arrange - Monitor current test process
        var currentProcessId = Environment.ProcessId;
        System.CreateProcessMonitoring(currentProcessId, samplingInterval: TimeSpan.FromMilliseconds(100));

        // Start BackgroundServices
        await System.ProcessMonitoring.StartAsync();

        // Act - Let it monitor for 1 second
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Stop BackgroundServices (allows metrics collection to complete)
        await System.ProcessMonitoring.StopAsync();

        // Assert
        Asserting.That(System.ProcessMonitoring.Monitor).HasCollectedMetrics();
    }

    [Fact]
    public async Task GetCollectedMetrics_WithFastSampling_ShouldCollectMultipleSamples()
    {
        // Arrange - Fast sampling to collect many samples quickly
        var currentProcessId = Environment.ProcessId;
        System.CreateProcessMonitoring(currentProcessId, samplingInterval: TimeSpan.FromMilliseconds(50));

        // Start monitoring
        await System.ProcessMonitoring.StartAsync();

        // Act - Monitor for 500ms (should get ~10 samples)
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Stop monitoring
        await System.ProcessMonitoring.StopAsync();

        // Assert - Should have at least 5 samples (conservative, allows for timing variance)
        Asserting.That(System.ProcessMonitoring.Monitor).HasAtLeastMetrics(5);
    }

    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldHaveValidMetrics()
    {
        // Arrange
        var currentProcessId = Environment.ProcessId;
        System.CreateProcessMonitoring(currentProcessId, samplingInterval: TimeSpan.FromMilliseconds(100));

        await System.ProcessMonitoring.StartAsync();

        // Act - Monitor and generate some load
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Do some work to ensure CPU usage
        for (int i = 0; i < 1000000; i++)
        {
            _ = Math.Sqrt(i);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await System.ProcessMonitoring.StopAsync();

        // Assert
        Asserting.That(System.ProcessMonitoring.Monitor).HasValidMetrics(currentProcessId);
    }

    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldHaveChronologicalTimestamps()
    {
        // Arrange
        var currentProcessId = Environment.ProcessId;
        System.CreateProcessMonitoring(currentProcessId, samplingInterval: TimeSpan.FromMilliseconds(100));

        await System.ProcessMonitoring.StartAsync();

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        await System.ProcessMonitoring.StopAsync();

        // Assert
        Asserting.That(System.ProcessMonitoring.Monitor).HasChronologicalTimestamps();
    }

    [Fact]
    public void AddProcessMonitoring_WithInvalidProcessId_ShouldThrow()
    {
        // Act & Assert
        Asserting.That(System).ThrowsArgumentOutOfRangeExceptionForInvalidProcessId(invalidProcessId: 0);
    }

    [Fact]
    public void AddProcessMonitoring_WithNegativeProcessId_ShouldThrow()
    {
        // Act & Assert
        Asserting.That(System).ThrowsArgumentOutOfRangeExceptionForInvalidProcessId(invalidProcessId: -1);
    }
}
