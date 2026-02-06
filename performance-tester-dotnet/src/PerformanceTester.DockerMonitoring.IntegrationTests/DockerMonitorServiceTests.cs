using JoanComasFdz.AssertingThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PerformanceTester.DockerMonitoring.IntegrationTests;

/// <summary>
/// Integration tests for DockerMonitorService BackgroundService.
/// Tests lifecycle management and metrics collection with real Docker containers
/// using streaming mode.
/// </summary>
public sealed class DockerMonitorServiceTests : IntegrationTest
{
    public DockerMonitorServiceTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task StartMonitoringAsync_WhenCalled_ShouldStartBackgroundServices()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();

        // Act
        await System.StartMonitoringAsync(phaseAwaiter);

        // Wait for connection (lifecycle phase)
        await phaseAwaiter.WaitForPhaseAsync(
            "performance-tester-postgres",
            DockerMonitorPhase.StreamConnected,
            DockerMonitorPhaseState.Completed);

        // Wait for samples (explicit, per-monitor)
        await System.PostgresMonitor.WaitForSampleCountAsync(1);
        await System.RabbitMqMonitor.WaitForSampleCountAsync(1);

        await System.StopMonitoringAsync();

        // Assert - should have collected metrics from both containers
        Asserting.That(System.PostgresMonitor).HasCollectedMetrics();
        Asserting.That(System.RabbitMqMonitor).HasCollectedMetrics();

        Output.WriteLine($"PostgreSQL: {System.PostgresMonitor.GetCollectedMetrics().Count} samples");
        Output.WriteLine($"RabbitMQ: {System.RabbitMqMonitor.GetCollectedMetrics().Count} samples");
    }

    [Fact]
    public async Task StopMonitoringAsync_WhenCalled_ShouldCompleteGracefully()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Wait for first sample (explicit, per-monitor)
        await System.PostgresMonitor.WaitForSampleCountAsync(1);
        await System.RabbitMqMonitor.WaitForSampleCountAsync(1);

        // Act
        await System.StopMonitoringAsync();

        // Assert - metrics should be retrievable after stop
        Asserting.That(System.PostgresMonitor).HasCollectedMetrics();
        Asserting.That(System.RabbitMqMonitor).HasCollectedMetrics();

        Output.WriteLine($"PostgreSQL: {System.PostgresMonitor.GetCollectedMetrics().Count} samples");
        Output.WriteLine($"RabbitMQ: {System.RabbitMqMonitor.GetCollectedMetrics().Count} samples");
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCollectMetricsOnEachDockerPush()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Act - Wait for exactly 3 samples (explicit, per-monitor)
        await System.PostgresMonitor.WaitForSampleCountAsync(3);
        await System.RabbitMqMonitor.WaitForSampleCountAsync(3);

        await System.StopMonitoringAsync();

        // Assert - now we know we have at least 3 samples
        Asserting.That(System.PostgresMonitor).HasMinimumSampleCount(expectedMinimum: 3);
        Asserting.That(System.RabbitMqMonitor).HasMinimumSampleCount(expectedMinimum: 3);
    }

    [Fact]
    public async Task DockerMonitorService_ShouldHandleContainerNotFound_Gracefully()
    {
        // Arrange - add monitoring for non-existent container
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDockerMonitoring("nonexistent-container");
        var host = builder.Build();
        var monitors = host.Services.GetServices<IDockerMonitor>().ToList();
        var nonexistentContainerMonitor = monitors.First(m => m.ContainerName == "nonexistent-container");

        // Act - should not throw
        await host.StartAsync();

        // Trigger the monitor to start
        await nonexistentContainerMonitor.StartMonitoringAsync(phaseAwaiter);

        // Wait for StreamFailed phase (container not found is now reported as StreamFailed)
        await phaseAwaiter.WaitForPhaseAsync(
            "nonexistent-container",
            DockerMonitorPhase.StreamFailed,
            DockerMonitorPhaseState.Failed);

        await host.StopAsync();

        // Assert - verify the phase was received and no metrics collected
        phaseAwaiter.AssertPhaseReceived(
            "nonexistent-container",
            DockerMonitorPhase.StreamFailed);
        Asserting.That(nonexistentContainerMonitor).HasNotCollectedMetrics();
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateCpuPercent_Correctly()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Wait for at least 1 sample to validate CPU calculations
        await System.PostgresMonitor.WaitForSampleCountAsync(1);
        await System.RabbitMqMonitor.WaitForSampleCountAsync(1);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasValidCpuPercentages();
        Asserting.That(System.RabbitMqMonitor).HasValidCpuPercentages();
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateMemoryMB_Correctly()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Wait for at least 1 sample to validate memory calculations
        await System.PostgresMonitor.WaitForSampleCountAsync(1);
        await System.RabbitMqMonitor.WaitForSampleCountAsync(1);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasValidMemoryMeasurements();
        Asserting.That(System.RabbitMqMonitor).HasValidMemoryMeasurements();
    }

    [Fact]
    public async Task GetCollectedMetrics_ShouldReturnChronologicalOrder()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Wait for at least 2 samples to verify chronological order
        await System.PostgresMonitor.WaitForSampleCountAsync(2);
        await System.RabbitMqMonitor.WaitForSampleCountAsync(2);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasMetricsInChronologicalOrder();
        Asserting.That(System.RabbitMqMonitor).HasMetricsInChronologicalOrder();
    }

    [Fact]
    public async Task ShouldReportStreamConnectedPhase()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();

        // Act
        await System.StartMonitoringAsync(phaseAwaiter);

        await phaseAwaiter.WaitForPhaseAsync(
            "performance-tester-postgres",
            DockerMonitorPhase.StreamConnected,
            DockerMonitorPhaseState.Completed);

        await System.StopMonitoringAsync();

        // Assert - verify lifecycle phases were received
        phaseAwaiter.AssertPhaseReceived(
            "performance-tester-postgres",
            DockerMonitorPhase.StreamConnecting);
        phaseAwaiter.AssertPhaseReceived(
            "performance-tester-postgres",
            DockerMonitorPhase.StreamConnected);
    }

    [Fact]
    public async Task ShouldReportMonitoringCompletedOnNormalShutdown()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        await System.PostgresMonitor.WaitForSampleCountAsync(2);

        // Act
        await System.StopMonitoringAsync();

        // Allow time for completion phase to be reported
        await Task.Delay(100);

        // Assert
        phaseAwaiter.AssertPhaseReceived(
            "performance-tester-postgres",
            DockerMonitorPhase.MonitoringCompleted);
    }
}
