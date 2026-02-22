using JoanComasFdz.AssertingThat;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests;

/// <summary>
/// Integration tests for ProcessMonitor functionality.
/// Tests the complete public API via named delegates with real process monitoring.
/// Monitors the current test process (self-monitoring pattern).
/// </summary>
public sealed class ProcessMonitorIntegrationTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoringCurrentProcess_ShouldCollectMetrics()
    {
        // Arrange - Monitor current test process
        var currentProcessId = ProcessId.FromInt(Environment.ProcessId);
        System.CreateProcessMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));

        // Start BackgroundServices
        await System.ProcessMonitoring.StartAsync();

        // Create phase awaiter for deterministic synchronization
        var phaseAwaiter = new ProcessMonitorPhaseAwaiter();

        // Start monitoring the current process (waits for first sample - returns after first sample collected)
        await System.ProcessMonitoring.StartMonitoringAsync(currentProcessId, phaseAwaiter.Report);

        // Stop BackgroundServices (allows metrics collection to complete)
        await System.ProcessMonitoring.StopAsync();

        // Assert
        Asserting.That(System.ProcessMonitoring.GetMetrics).HasCollectedMetrics();
        phaseAwaiter.AssertFirstSampleCollectedReceived();
    }

    [Fact]
    public async Task GetCollectedMetrics_WithFastSampling_ShouldCollectMultipleSamples()
    {
        // Arrange - Fast sampling to collect many samples quickly
        var currentProcessId = ProcessId.FromInt(Environment.ProcessId);
        System.CreateProcessMonitoring(samplingInterval: TimeSpan.FromMilliseconds(50));

        // Start BackgroundService
        await System.ProcessMonitoring.StartAsync();

        // Create phase awaiter for deterministic synchronization
        var phaseAwaiter = new ProcessMonitorPhaseAwaiter();

        // Start monitoring the current process
        await System.ProcessMonitoring.StartMonitoringAsync(currentProcessId, phaseAwaiter.Report);

        // Act - Wait for exactly 5 samples (deterministic, no timing assumption)
        await phaseAwaiter.WaitForSampleCountAsync(minimumSampleCount: 5);

        // Stop monitoring
        await System.ProcessMonitoring.StopAsync();

        // Assert - Verify we have at least 5 samples
        Asserting.That(System.ProcessMonitoring.GetMetrics).HasAtLeastMetrics(5);
        phaseAwaiter.AssertSampleCountAtLeast(5);
    }

    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldHaveValidMetrics()
    {
        // Arrange
        var currentProcessId = ProcessId.FromInt(Environment.ProcessId);
        System.CreateProcessMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));

        await System.ProcessMonitoring.StartAsync();

        // Create phase awaiter for deterministic synchronization
        var phaseAwaiter = new ProcessMonitorPhaseAwaiter();

        // Start monitoring
        await System.ProcessMonitoring.StartMonitoringAsync(currentProcessId, phaseAwaiter.Report);

        // Wait for first sample before generating load
        await phaseAwaiter.WaitForSampleCountAsync(minimumSampleCount: 1);

        // Act - Generate some CPU load
        for (int i = 0; i < 1000000; i++)
        {
            _ = Math.Sqrt(i);
        }

        // Wait for more samples after load (to capture CPU metrics during load)
        await phaseAwaiter.WaitForSampleCountAsync(minimumSampleCount: 3);

        await System.ProcessMonitoring.StopAsync();

        // Assert
        Asserting.That(System.ProcessMonitoring.GetMetrics).HasValidMetrics(currentProcessId.Value);
    }

    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldHaveChronologicalTimestamps()
    {
        // Arrange
        var currentProcessId = ProcessId.FromInt(Environment.ProcessId);
        System.CreateProcessMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));

        await System.ProcessMonitoring.StartAsync();

        // Create phase awaiter for deterministic synchronization
        var phaseAwaiter = new ProcessMonitorPhaseAwaiter();

        // Start monitoring
        await System.ProcessMonitoring.StartMonitoringAsync(currentProcessId, phaseAwaiter.Report);

        // Act - Wait for at least 3 samples to verify chronological order
        await phaseAwaiter.WaitForSampleCountAsync(minimumSampleCount: 3);

        await System.ProcessMonitoring.StopAsync();

        // Assert
        Asserting.That(System.ProcessMonitoring.GetMetrics).HasChronologicalTimestamps();
    }

    [Fact]
    public void AddProcessMonitoring_WithInvalidProcessId_ShouldReject()
    {
        // Act & Assert
        Asserting.That(System).RejectsInvalidProcessId(invalidProcessId: 0);
    }

    [Fact]
    public void AddProcessMonitoring_WithNegativeProcessId_ShouldReject()
    {
        // Act & Assert
        Asserting.That(System).RejectsInvalidProcessId(invalidProcessId: -1);
    }
}
