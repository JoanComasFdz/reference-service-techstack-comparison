using JoanComasFdz.AssertingThat;
using PerformanceTester.IntegrationTesting;
using Xunit;

namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for Reporting integration tests.
/// Follows Asserting.That pattern (see CLAUDE.md).
/// </summary>
internal static class ReportingAssertions
{
    /// <summary>
    /// Asserts that system information was detected successfully.
    /// </summary>
    public static async Task HasSystemInfo(
        this AssertingThat<ISystemInfoDetector> assertingThat)
    {
        var systemInfo = await assertingThat.InstanceToAssert.GetSystemInfoAsync();

        Assert.NotNull(systemInfo);
        Assert.NotNull(systemInfo.Os);
        Assert.NotNull(systemInfo.OsVersion);
        Assert.NotNull(systemInfo.Cpu);
        Assert.True(systemInfo.Cpu.LogicalProcessors > 0);
        Assert.NotNull(systemInfo.Ram);
        Assert.True(systemInfo.Ram.TotalGb > 0);
    }

    /// <summary>
    /// Asserts that CPU information has all fields populated with consistent values.
    /// Validates model name exists, processor counts are positive, and logical >= physical.
    /// </summary>
    public static async Task HasPopulatedCpuInfoWithConsistentProcessorCounts(
        this AssertingThat<ISystemInfoDetector> assertingThat)
    {
        var systemInfo = await assertingThat.InstanceToAssert.GetSystemInfoAsync();

        Assert.NotNull(systemInfo);
        Assert.NotNull(systemInfo.Cpu);
        Assert.NotEmpty(systemInfo.Cpu.Model);
        Assert.True(systemInfo.Cpu.LogicalProcessors > 0);
        Assert.True(systemInfo.Cpu.PhysicalProcessors > 0);
        Assert.True(systemInfo.Cpu.LogicalProcessors >= systemInfo.Cpu.PhysicalProcessors);
    }

    /// <summary>
    /// Asserts that RAM information has total GB within reasonable bounds (0 < totalGb < 10000).
    /// </summary>
    public static async Task HasRamInfoWithReasonableTotalGb(
        this AssertingThat<ISystemInfoDetector> assertingThat)
    {
        var systemInfo = await assertingThat.InstanceToAssert.GetSystemInfoAsync();

        Assert.NotNull(systemInfo);
        Assert.NotNull(systemInfo.Ram);
        Assert.True(systemInfo.Ram.TotalGb > 0);
        Assert.True(systemInfo.Ram.TotalGb < 10000); // Reasonable upper bound
    }

    /// <summary>
    /// Asserts that disk information has all required fields populated.
    /// Validates each disk has non-empty name, size, and type.
    /// </summary>
    public static async Task HasDisksWithPopulatedFieldsAndPositiveSizes(
        this AssertingThat<ISystemInfoDetector> assertingThat)
    {
        var systemInfo = await assertingThat.InstanceToAssert.GetSystemInfoAsync();

        Assert.NotNull(systemInfo);
        Assert.NotNull(systemInfo.Disks);

        foreach (var disk in systemInfo.Disks)
        {
            Assert.NotEmpty(disk.Name);
            Assert.NotEmpty(disk.Size);
            Assert.NotEmpty(disk.Type);
        }
    }
}
