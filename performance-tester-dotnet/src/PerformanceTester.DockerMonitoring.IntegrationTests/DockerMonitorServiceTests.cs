using JoanComasFdz.AssertingThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit.Abstractions;

namespace PerformanceTester.DockerMonitoring.IntegrationTests;

/// <summary>
/// Integration tests for DockerMonitorService BackgroundService.
/// Tests lifecycle management and metrics collection with real Docker containers
/// using streaming mode.
/// </summary>
public sealed class DockerMonitorServiceTests : IntegrationTest
{
    private static readonly NonEmptyString PostgresName = DockerMonitoringSystem.PostgresName;
    private static readonly NonEmptyString RabbitMqName = DockerMonitoringSystem.RabbitMqName;

    public DockerMonitorServiceTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task StartMonitoringAsync_WhenCalled_ShouldStartBackgroundServices()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();

        // Act
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        // Wait for connection (lifecycle phase)
        await phaseAwaiter.WaitForPhaseAsync(
            "performance-tester-postgres",
            DockerMonitorPhase.StreamConnected,
            DockerMonitorPhaseState.Completed);

        // Wait for samples (explicit, per-container)
        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 1);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 1);

        await System.StopMonitoringAsync();

        // Assert - should have collected metrics from both containers
        Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(PostgresName);
        Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(RabbitMqName);

        Output.WriteLine($"PostgreSQL: {System.GetDockerMetrics(PostgresName).Count} samples");
        Output.WriteLine($"RabbitMQ: {System.GetDockerMetrics(RabbitMqName).Count} samples");
    }

    [Fact]
    public async Task StopMonitoringAsync_WhenCalled_ShouldCompleteGracefully()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        // Wait for first sample (explicit, per-container)
        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 1);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 1);

        // Act
        await System.StopMonitoringAsync();

        // Assert - metrics should be retrievable after stop
        Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(PostgresName);
        Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(RabbitMqName);

        Output.WriteLine($"PostgreSQL: {System.GetDockerMetrics(PostgresName).Count} samples");
        Output.WriteLine($"RabbitMQ: {System.GetDockerMetrics(RabbitMqName).Count} samples");
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCollectMetricsOnEachDockerPush()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        // Act - Wait for exactly 3 samples (explicit, per-container)
        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 3);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 3);

        await System.StopMonitoringAsync();

        // Assert - now we know we have at least 3 samples
        Asserting.That(System.GetDockerMetrics).HasMinimumSampleCountFor(PostgresName, expectedMinimum: 3);
        Asserting.That(System.GetDockerMetrics).HasMinimumSampleCountFor(RabbitMqName, expectedMinimum: 3);
    }

    [Fact]
    public async Task DockerMonitorService_ShouldHandleContainerNotFound_Gracefully()
    {
        // Arrange - add monitoring for non-existent container
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        var nonexistentRmq = RabbitMqContainerName.FromString("nonexistent-container");
        var nonexistentPg = PostgresContainerName.FromString("nonexistent-container-pg");

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDockerMonitoring(nonexistentRmq, nonexistentPg);
        var host = builder.Build();

        var startDockerMonitoring = host.Services.GetRequiredService<StartDockerMonitoringDelegate>();
        var getDockerMetrics = host.Services.GetRequiredService<GetDockerMetricsDelegate>();

        // Act - should not throw
        await host.StartAsync();
        await startDockerMonitoring(phaseAwaiter.Report, CancellationToken.None);

        // Wait for StreamFailed phase (container not found is reported as StreamFailed)
        await phaseAwaiter.WaitForPhaseAsync(
            "nonexistent-container",
            DockerMonitorPhase.StreamFailed,
            DockerMonitorPhaseState.Failed);

        await host.StopAsync();

        // Assert - verify the phase was received and no metrics collected
        phaseAwaiter.AssertPhaseReceived(
            "nonexistent-container",
            DockerMonitorPhase.StreamFailed);
        Asserting.That(getDockerMetrics).HasNotCollectedMetricsFor(nonexistentRmq);
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateCpuPercent_Correctly()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        // Wait for at least 1 sample to validate CPU calculations
        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 1);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 1);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.GetDockerMetrics).HasValidCpuPercentagesFor(PostgresName);
        Asserting.That(System.GetDockerMetrics).HasValidCpuPercentagesFor(RabbitMqName);
    }

    [Fact]
    public async Task DockerMonitorService_ShouldCalculateMemoryMB_Correctly()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        // Wait for at least 1 sample to validate memory calculations
        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 1);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 1);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.GetDockerMetrics).HasValidMemoryMeasurementsFor(PostgresName);
        Asserting.That(System.GetDockerMetrics).HasValidMemoryMeasurementsFor(RabbitMqName);
    }

    [Fact]
    public async Task GetCollectedMetrics_ShouldReturnChronologicalOrder()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        // Wait for at least 2 samples to verify chronological order
        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 2);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 2);

        await System.StopMonitoringAsync();

        // Assert
        Asserting.That(System.GetDockerMetrics).HasMetricsInChronologicalOrderFor(PostgresName);
        Asserting.That(System.GetDockerMetrics).HasMetricsInChronologicalOrderFor(RabbitMqName);
    }

    [Fact]
    public async Task ShouldReportStreamConnectedPhase()
    {
        // Arrange
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();

        // Act
        await System.StartMonitoringAsync(phaseAwaiter.Report);

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
        await System.StartMonitoringAsync(phaseAwaiter.Report);

        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 2);

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
