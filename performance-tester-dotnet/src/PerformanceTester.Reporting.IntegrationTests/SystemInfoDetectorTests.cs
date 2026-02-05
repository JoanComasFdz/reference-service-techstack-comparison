using JoanComasFdz.AssertingThat;
using PerformanceTester.IntegrationTesting;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests;

/// <summary>
/// Integration tests for ISystemInfoDetector (platform-specific detection).
/// Tests verify cross-platform hardware detection works correctly.
/// </summary>
public sealed class SystemInfoDetectorTests : IntegrationTest
{
    public SystemInfoDetectorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task GetSystemInfoAsync_ShouldDetectSystemInformation()
    {
        // Act & Assert
        await Asserting.That(System.Reporting.SystemInfoDetector).HasSystemInfo();
    }

    [Fact]
    public async Task GetSystemInfoAsync_ShouldDetectPopulatedCpuInfo()
    {
        // Act & Assert
        await Asserting.That(System.Reporting.SystemInfoDetector).HasPopulatedCpuInfoWithConsistentProcessorCounts();
    }

    [Fact]
    public async Task GetSystemInfoAsync_ShouldDetectRamInfoWithReasonableTotalGb()
    {
        // Act & Assert
        await Asserting.That(System.Reporting.SystemInfoDetector).HasRamInfoWithReasonableTotalGb();
    }

    [Fact]
    public async Task GetSystemInfoAsync_ShouldDetectDisksWithPopulatedFields()
    {
        // Act & Assert
        await Asserting.That(System.Reporting.SystemInfoDetector).HasDisksWithPopulatedFieldsAndPositiveSizes();
    }

    [Fact]
    public async Task GetSystemInfoAsync_ShouldCacheResults()
    {
        // Act
        var result1 = await System.Reporting.SystemInfoDetector.GetSystemInfoAsync();
        var result2 = await System.Reporting.SystemInfoDetector.GetSystemInfoAsync();

        // Assert
        Assert.Same(result1, result2); // Should return same instance (cached)
    }

    [Fact]
    public async Task GetSystemInfoAsync_ShouldDetectOperatingSystem()
    {
        // Act
        var systemInfo = await System.Reporting.SystemInfoDetector.GetSystemInfoAsync();

        // Assert
        Assert.NotNull(systemInfo);
        Assert.NotNull(systemInfo.Os);
        Assert.True(systemInfo.Os == "Windows" || systemInfo.Os == "Linux");
    }

    [Fact]
    public async Task GetSystemInfoAsync_OnLinux_ShouldReturnDistroNameInOsRelease()
    {
        // Skip on Windows
        if (!OperatingSystem.IsLinux())
        {
            Output.WriteLine("Skipping test - only runs on Linux");
            return;
        }

        // Act
        var systemInfo = await System.Reporting.SystemInfoDetector.GetSystemInfoAsync();

        // Assert
        Assert.NotNull(systemInfo);

        // OsRelease should contain distro name (e.g., "Ubuntu 24.04" or "Debian 12")
        // NOT just kernel version like "6.6.87.2"
        Assert.NotNull(systemInfo.OsRelease);
        Assert.DoesNotMatch(@"^\d+\.\d+\.\d+(\.\d+)?$", systemInfo.OsRelease); // Should NOT be just a version number

        // Should contain actual distro info
        var validDistros = new[] { "Ubuntu", "Debian", "Fedora", "CentOS", "Rocky", "Alma", "RHEL", "Arch", "Alpine", "openSUSE" };
        var containsDistro = validDistros.Any(d => systemInfo.OsRelease.Contains(d, StringComparison.OrdinalIgnoreCase));
        Assert.True(containsDistro, $"OsRelease '{systemInfo.OsRelease}' should contain a known Linux distribution name");

        Output.WriteLine($"Detected OS Release: {systemInfo.OsRelease}");
    }
}
