using JoanComasFdz.AssertingThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PerformanceTester.DockerMonitoring.IntegrationTests;

/// <summary>
/// Integration tests for DockerMonitorService BackgroundService.
/// Tests lifecycle management and metrics collection with real Docker containers.
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

        // Wait for at least 1 sample from each container (deterministic)
        await phaseAwaiter.WaitForSampleCountAsync(
            ["performance-tester-postgres", "performance-tester-rabbitmq"],
            minimumSampleCount: 1);

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

        // Wait for first sample (deterministic, no polling)
        await phaseAwaiter.WaitForSampleCountAsync(
            ["performance-tester-postgres", "performance-tester-rabbitmq"],
            minimumSampleCount: 1);

        // Act
        await System.StopMonitoringAsync();

        // Assert - metrics should be retrievable after stop
        Asserting.That(System.PostgresMonitor).HasCollectedMetrics();
        Asserting.That(System.RabbitMqMonitor).HasCollectedMetrics();

        Output.WriteLine($"PostgreSQL: {System.PostgresMonitor.GetCollectedMetrics().Count} samples");
        Output.WriteLine($"RabbitMQ: {System.RabbitMqMonitor.GetCollectedMetrics().Count} samples");
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCollectMetricsAtRegularIntervals()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Act - Wait for exactly 3 samples (enough to verify interval collection)
        await phaseAwaiter.WaitForSampleCountAsync("performance-tester-postgres", minimumSampleCount: 3);
        await phaseAwaiter.WaitForSampleCountAsync("performance-tester-rabbitmq", minimumSampleCount: 3);

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

        // FIX: Actually trigger the monitor to start (was missing in original test!)
        await nonexistentContainerMonitor.StartMonitoringAsync(phaseAwaiter);

        // Wait for ContainerNotFound phase (deterministic)
        await phaseAwaiter.WaitForPhaseAsync(
            "nonexistent-container",
            DockerMonitorPhase.ContainerNotFound,
            DockerMonitorPhaseState.Completed);

        await host.StopAsync();

        // Assert - verify the phase was received and no metrics collected
        phaseAwaiter.AssertContainerNotFoundReceived("nonexistent-container");
        Asserting.That(nonexistentContainerMonitor).HasNotCollectedMetrics();
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateCpuPercent_Correctly()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter);

        // Wait for at least 1 sample to validate CPU calculations
        await phaseAwaiter.WaitForSampleCountAsync(
            ["performance-tester-postgres", "performance-tester-rabbitmq"],
            minimumSampleCount: 1);

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
        await phaseAwaiter.WaitForSampleCountAsync(
            ["performance-tester-postgres", "performance-tester-rabbitmq"],
            minimumSampleCount: 1);

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
        await phaseAwaiter.WaitForSampleCountAsync(
            ["performance-tester-postgres", "performance-tester-rabbitmq"],
            minimumSampleCount: 2);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasMetricsInChronologicalOrder();
        Asserting.That(System.RabbitMqMonitor).HasMetricsInChronologicalOrder();
    }
}
