using JoanComasFdz.AssertingThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        // Act
        await System.StartMonitoringAsync();

        // Give monitors time to collect some samples
        // Docker stats API is slow (each call ~2-3 seconds with our 100ms delay)
        await Task.Delay(TimeSpan.FromSeconds(3));

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
        await System.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

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
        await System.StartMonitoringAsync();

        // Act - collect for 5 seconds with 500ms interval
        // First tick after 500ms, then ~8-9 more samples possible
        // But Docker stats API takes ~200ms per call, so realistic is ~5-7 samples
        await Task.Delay(TimeSpan.FromSeconds(5));
        await System.StopMonitoringAsync();

        // Assert - be very conservative (Docker stats API is slow, ~2-3 samples realistic)
        Asserting.That(System.PostgresMonitor).HasMinimumSampleCount(expectedMinimum: 2);
        Asserting.That(System.RabbitMqMonitor).HasMinimumSampleCount(expectedMinimum: 2);
    }

    [Fact]
    public async Task DockerMonitorService_ShouldHandleContainerNotFound_Gracefully()
    {
        // Arrange - add monitoring for non-existent container
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDockerMonitoring("nonexistent-container");
        var host = builder.Build();
        var monitors = host.Services.GetServices<IDockerMonitor>().ToList();
        var nonexistentContainerMonitor = monitors.First(m => m.ContainerName == "nonexistent-container");

        // Act - should not throw
        await host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await host.StopAsync();

        // Assert - no exceptions thrown
        Asserting.That(nonexistentContainerMonitor).HasNotCollectedMetrics();
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateCpuPercent_Correctly()
    {
        // Arrange
        await System.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasValidCpuPercentages();
        Asserting.That(System.RabbitMqMonitor).HasValidCpuPercentages();
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateMemoryMB_Correctly()
    {
        // Arrange
        await System.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasValidMemoryMeasurements();
        Asserting.That(System.RabbitMqMonitor).HasValidMemoryMeasurements();
    }

    [Fact]
    public async Task GetCollectedMetrics_ShouldReturnChronologicalOrder()
    {
        // Arrange
        await System.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.PostgresMonitor).HasMetricsInChronologicalOrder();
        Asserting.That(System.RabbitMqMonitor).HasMetricsInChronologicalOrder();
    }
}
