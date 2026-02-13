using System.Text.Json;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using PerformanceTester.Reporting.ValueObjects;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for system metrics report (*.system-metrics.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points:
/// - Root has "cpu_count" field (integer)
/// - Root has "is_wsl2" field (boolean) at the END
/// - Samples have: memory_used_mb, memory_total_mb, memory_percent
/// - Summary has: min_cpu_percent (unique to system metrics)
/// - Summary has: avg/peak for CPU and memory
/// - Sampling interval: 500ms
/// </summary>
public sealed class SystemMetricsContractTests : IntegrationTest
{
    public SystemMetricsContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of system metrics JSON.
    /// </summary>
    [Fact]
    public async Task SystemMetrics_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // SystemResourceSample has extended memory fields (used, total, percent)
        const double totalMemoryMb = 15676.19;
        var systemSamples = new List<SystemResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.5), ElapsedSeconds = 0.5, CpuPercent = 0.0, MemoryUsedMb = 3443.96, MemoryTotalMb = totalMemoryMb, MemoryPercent = 21.97 },
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, CpuPercent = 2.5, MemoryUsedMb = 3452.02, MemoryTotalMb = totalMemoryMb, MemoryPercent = 22.02 },
            new() { Timestamp = testDate.AddSeconds(1.5), ElapsedSeconds = 1.5, CpuPercent = 8.3, MemoryUsedMb = 3450.85, MemoryTotalMb = totalMemoryMb, MemoryPercent = 22.01 },
            new() { Timestamp = testDate.AddSeconds(2.0), ElapsedSeconds = 2.0, CpuPercent = 11.1, MemoryUsedMb = 3454.20, MemoryTotalMb = totalMemoryMb, MemoryPercent = 22.03 },
            new() { Timestamp = testDate.AddSeconds(2.5), ElapsedSeconds = 2.5, CpuPercent = 35.9, MemoryUsedMb = 3584.81, MemoryTotalMb = totalMemoryMb, MemoryPercent = 22.87 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.system-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify EVERY aspect of the contract
            // ═══════════════════════════════════════════════════════════

            // --- ROOT LEVEL FIELDS ---
            AssertTimestampFormat(root, "test_date", TimestampFormat.TestDate);

            // CONTRACT: System metrics has cpu_count at root level
            AssertFieldExists(root, "cpu_count", "root");
            var cpuCount = root.GetProperty("cpu_count").GetInt32();
            Assert.True(cpuCount > 0, $"cpu_count should be > 0 but was {cpuCount}");

            AssertIntegerValue(root, "sampling_interval_ms", 500);

            // CONTRACT: System metrics has is_wsl2 flag at END
            AssertFieldExists(root, "is_wsl2", "root");
            // Note: Value depends on system, just verify it exists

            // --- SAMPLES ARRAY ---
            AssertArrayCount(root, "samples", 5);
            var samples = root.GetProperty("samples");
            var firstSample = samples[0];

            // CONTRACT: System samples have specific field names
            AssertFieldExists(firstSample, "timestamp", "sample[0]");
            AssertFieldExists(firstSample, "cpu_percent", "sample[0]");
            AssertFieldExists(firstSample, "memory_used_mb", "sample[0]");
            AssertFieldExists(firstSample, "memory_total_mb", "sample[0]");
            AssertFieldExists(firstSample, "memory_percent", "sample[0]");

            // Verify system samples do NOT have threads (that's for process metrics)
            AssertFieldNotExists(firstSample, "threads", "sample[0]");

            // Verify system samples do NOT have plain memory_mb (use memory_used_mb instead)
            AssertFieldNotExists(firstSample, "memory_mb", "sample[0]");
            AssertFieldNotExists(firstSample, "memory_rss_mb", "sample[0]");

            // CONTRACT: Timestamp format should be ISO8601 without timezone
            AssertTimestampFormat(firstSample, "timestamp", TimestampFormat.Iso8601WithMicroseconds);

            // --- SUMMARY OBJECT ---
            var summary = AssertObjectExists(root, "summary");

            // CONTRACT: System metrics summary has min_cpu_percent (unique to system metrics)
            AssertFieldExists(summary, "avg_cpu_percent");
            AssertFieldExists(summary, "peak_cpu_percent");
            AssertFieldExists(summary, "min_cpu_percent");  // UNIQUE to system metrics
            AssertFieldExists(summary, "avg_memory_used_mb");
            AssertFieldExists(summary, "peak_memory_used_mb");
            AssertFieldExists(summary, "avg_memory_percent");
            AssertFieldExists(summary, "peak_memory_percent");
            AssertFieldExists(summary, "total_samples");

            // Verify calculations
            // Peak CPU: max(0.0, 2.5, 8.3, 11.1, 35.9) = 35.9
            AssertNumericValue(summary, "peak_cpu_percent", 35.9, 2);

            // Min CPU: min(0.0, 2.5, 8.3, 11.1, 35.9) = 0.0
            AssertNumericValue(summary, "min_cpu_percent", 0.0, 2);

            // Peak Memory: max(3443.96, 3452.02, 3450.85, 3454.20, 3584.81) = 3584.81
            AssertNumericValue(summary, "peak_memory_used_mb", 3584.81, 2);

            // Total samples: 5
            AssertIntegerValue(summary, "total_samples", 5);

            Output.WriteLine("✓ System metrics contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: cpu_count must be present at root level.
    /// </summary>
    [Fact]
    public async Task SystemMetrics_CpuCount_ShouldBePresent()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var systemSamples = ResourceSampleBuilder.CreateSystem(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(new SystemInfo
            {
                Os = "Linux",
                OsRelease = "6.6.87.2-microsoft-standard-WSL2",
                OsVersion = "#1",
                WslVersion = "WSL2",
                Cpu = new CpuInfo
                {
                    Model = "Intel Core i9",
                    LogicalProcessors = 16,  // This should be cpu_count
                    PhysicalProcessors = 1,
                    SpeedMhz = 4000.0
                },
                Ram = new RamInfo { TotalGb = 32.0 },
                Disks = new List<DiskInfo>()
            })
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.system-metrics.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var root = doc.RootElement;

            // CONTRACT: cpu_count at root level
            AssertFieldExists(root, "cpu_count", "root");
            var cpuCount = root.GetProperty("cpu_count").GetInt32();
            Assert.Equal(16, cpuCount);

            Output.WriteLine($"✓ cpu_count verified: {cpuCount}");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: is_wsl2 must be present at root level.
    /// </summary>
    [Fact]
    public async Task SystemMetrics_IsWsl2_ShouldBePresent()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var systemSamples = ResourceSampleBuilder.CreateSystem(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(new SystemInfo
            {
                Os = "Linux",
                OsRelease = "6.6.87.2-microsoft-standard-WSL2",
                OsVersion = "#1",
                WslVersion = "WSL2",  // This indicates WSL2
                Cpu = new CpuInfo { Model = "Intel", LogicalProcessors = 8, PhysicalProcessors = 1, SpeedMhz = 3000.0 },
                Ram = new RamInfo { TotalGb = 16.0 },
                Disks = new List<DiskInfo>()
            })
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.system-metrics.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var root = doc.RootElement;

            // CONTRACT: is_wsl2 at root level (boolean)
            AssertFieldExists(root, "is_wsl2", "root");
            var isWsl2 = root.GetProperty("is_wsl2").GetBoolean();
            Assert.True(isWsl2, "is_wsl2 should be true when WslVersion is 'WSL2'");

            Output.WriteLine($"✓ is_wsl2 verified: {isWsl2}");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: min_cpu_percent is unique to system metrics.
    /// </summary>
    [Fact]
    public async Task SystemMetrics_MinCpuPercent_ShouldBeInSummary()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        const double totalMemoryMb = 16000.0;
        var systemSamples = new List<SystemResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.5), ElapsedSeconds = 0.5, CpuPercent = 5.0, MemoryUsedMb = 3000.0, MemoryTotalMb = totalMemoryMb, MemoryPercent = 18.75 },
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, CpuPercent = 10.0, MemoryUsedMb = 3100.0, MemoryTotalMb = totalMemoryMb, MemoryPercent = 19.38 },
            new() { Timestamp = testDate.AddSeconds(1.5), ElapsedSeconds = 1.5, CpuPercent = 3.0, MemoryUsedMb = 3050.0, MemoryTotalMb = totalMemoryMb, MemoryPercent = 19.06 }
        };
        // Min CPU should be 3.0

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.system-metrics.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            // CONTRACT: min_cpu_percent is present (unique to system metrics)
            AssertFieldExists(summary, "min_cpu_percent");
            AssertNumericValue(summary, "min_cpu_percent", 3.0, 2);

            Output.WriteLine("✓ min_cpu_percent verified (unique to system metrics)");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: sample field order must match Python.
    /// </summary>
    [Fact]
    public async Task SystemMetrics_SampleFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var systemSamples = ResourceSampleBuilder.CreateSystem(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.system-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract a sample section
            var sampleStart = rawJson.IndexOf("\"timestamp\"");
            var sampleEnd = rawJson.IndexOf("}", sampleStart) + 1;
            var sampleSection = rawJson.Substring(sampleStart - 5, sampleEnd - sampleStart + 5);

            // CONTRACT: Python sample field order for system metrics
            AssertFieldOrder(sampleSection, "timestamp", "cpu_percent", "memory_used_mb", "memory_total_mb", "memory_percent");

            Output.WriteLine("✓ System sample field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Root field order including is_wsl2 at the end.
    /// </summary>
    [Fact]
    public async Task SystemMetrics_RootFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var systemSamples = ResourceSampleBuilder.CreateSystem(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.system-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // CONTRACT: Python root field order for system metrics
            // is_wsl2 should be at the END
            AssertFieldOrder(rawJson, "test_date", "cpu_count", "sampling_interval_ms", "samples", "summary", "is_wsl2");

            Output.WriteLine("✓ System metrics root field order verified (is_wsl2 at end)");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
