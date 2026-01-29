using JoanComasFdz.AssertingThat;
using PerformanceTester.SystemMonitoring.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.SystemMonitoring.IntegrationTests;

/// <summary>
/// Integration tests for SystemMonitor functionality.
/// Tests the complete public API with real /proc filesystem reads.
/// </summary>
public sealed class SystemMonitorIntegrationTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldCollectMetrics()
    {
        // Arrange
        System.CreateSystemMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));
        await System.SystemMonitoring.StartAsync();

        // Act - Start monitoring and wait
        await System.SystemMonitoring.Monitor.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await System.SystemMonitoring.StopAsync();

        // Assert
        Asserting.That(System.SystemMonitoring.Monitor).HasCollectedMetrics();
    }

    [Fact]
    public async Task GetCollectedMetrics_WithFastSampling_ShouldCollectMultipleSamples()
    {
        // Arrange - Fast sampling
        System.CreateSystemMonitoring(samplingInterval: TimeSpan.FromMilliseconds(50));
        await System.SystemMonitoring.StartAsync();

        // Act - Monitor for 500ms
        await System.SystemMonitoring.Monitor.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await System.SystemMonitoring.StopAsync();

        // Assert - Should have at least 5 samples
        Asserting.That(System.SystemMonitoring.Monitor).HasAtLeastMetrics(5);
    }

    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldHaveValidMetrics()
    {
        // Arrange
        System.CreateSystemMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));
        await System.SystemMonitoring.StartAsync();

        // Act
        await System.SystemMonitoring.Monitor.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await System.SystemMonitoring.StopAsync();

        // Assert
        Asserting.That(System.SystemMonitoring.Monitor).HasValidMetrics();
    }

    [Fact]
    public async Task GetCollectedMetrics_WhenMonitoring_ShouldHaveChronologicalTimestamps()
    {
        // Arrange
        System.CreateSystemMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));
        await System.SystemMonitoring.StartAsync();

        // Act
        await System.SystemMonitoring.Monitor.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await System.SystemMonitoring.StopAsync();

        // Assert
        Asserting.That(System.SystemMonitoring.Monitor).HasChronologicalTimestamps();
    }

    [Fact]
    public async Task CpuCount_ShouldBePositive()
    {
        // Arrange
        System.CreateSystemMonitoring();
        await System.SystemMonitoring.StartAsync();

        // Act & Assert
        Asserting.That(System.SystemMonitoring.Monitor).HasValidCpuCount();

        await System.SystemMonitoring.StopAsync();
    }

    [Fact]
    public async Task IsWsl2_ShouldBeDetectable()
    {
        // Arrange
        System.CreateSystemMonitoring();
        await System.SystemMonitoring.StartAsync();

        // Act
        var isWsl2 = System.SystemMonitoring.Monitor.IsWsl2;

        // Assert - Just verify it doesn't throw
        Output.WriteLine($"IsWsl2: {isWsl2}");

        await System.SystemMonitoring.StopAsync();
    }

    [Fact]
    public async Task GetCollectedMetrics_BeforeStartMonitoringAsync_ShouldReturnEmpty()
    {
        // Arrange
        System.CreateSystemMonitoring();
        await System.SystemMonitoring.StartAsync();

        // Act - Don't call StartMonitoringAsync
        var metrics = System.SystemMonitoring.Monitor.GetCollectedMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Empty(metrics);

        await System.SystemMonitoring.StopAsync();
    }

    [Fact]
    public async Task MemoryMetrics_ShouldHaveValidValues()
    {
        // Arrange
        System.CreateSystemMonitoring(samplingInterval: TimeSpan.FromMilliseconds(100));
        await System.SystemMonitoring.StartAsync();

        // Act
        await System.SystemMonitoring.Monitor.StartMonitoringAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await System.SystemMonitoring.StopAsync();

        // Assert
        var metrics = System.SystemMonitoring.Monitor.GetCollectedMetrics();
        Assert.NotEmpty(metrics);

        var sample = metrics.First();
        Output.WriteLine($"Memory: {sample.MemoryUsedMb:F2} MB / {sample.MemoryTotalMb:F2} MB ({sample.MemoryPercent:F1}%)");

        Assert.True(sample.MemoryTotalMb > 1000, "Total memory should be > 1GB");
        Assert.True(sample.MemoryUsedMb > 0, "Used memory should be > 0");
    }
}
