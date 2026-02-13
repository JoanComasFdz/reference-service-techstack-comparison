using System.Text.Json;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using PerformanceTester.Reporting.ValueObjects;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for process resource metrics report (*.resource-metrics.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points:
/// - Root has "process_info" object (NOT "container_info")
/// - process_info contains: pid, name, port
/// - Sample fields use "memory_rss_mb" (NOT "memory_mb")
/// - Samples include "threads" field
/// - Sampling interval: 500ms
/// - Summary has: avg_cpu_percent, peak_cpu_percent, avg_memory_rss_mb, peak_memory_rss_mb, total_samples
/// </summary>
public sealed class ResourceMetricsContractTests : IntegrationTest
{
    public ResourceMetricsContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of process resource metrics JSON.
    /// </summary>
    [Fact]
    public async Task ResourceMetrics_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        var processSamples = new List<ProcessResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.5), ElapsedSeconds = 0.5, CpuPercent = 0.0, MemoryRssMb = 120.86, Threads = 35 },
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, CpuPercent = 69.9, MemoryRssMb = 124.11, Threads = 35 },
            new() { Timestamp = testDate.AddSeconds(1.5), ElapsedSeconds = 1.5, CpuPercent = 121.7, MemoryRssMb = 128.98, Threads = 35 },
            new() { Timestamp = testDate.AddSeconds(2.0), ElapsedSeconds = 2.0, CpuPercent = 93.9, MemoryRssMb = 133.48, Threads = 35 },
            new() { Timestamp = testDate.AddSeconds(2.5), ElapsedSeconds = 2.5, CpuPercent = 95.9, MemoryRssMb = 135.11, Threads = 35 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("dotnet9ReferenceService")
            .WithProcessResourceSamples(processSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.resource-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify EVERY aspect of the contract
            // ═══════════════════════════════════════════════════════════

            // --- ROOT LEVEL FIELDS ---
            AssertTimestampFormat(root, "test_date", TimestampFormat.TestDate);
            AssertIntegerValue(root, "sampling_interval_ms", 500);

            // --- ROOT FIELD ORDER ---
            AssertFieldOrder(rawJson, "test_date", "process_info", "sampling_interval_ms", "samples", "summary");

            // --- PROCESS_INFO OBJECT (NOT container_info) ---
            var processInfo = AssertObjectExists(root, "process_info");

            // CONTRACT: Python uses "process_info" for process metrics
            AssertFieldNotExists(root, "container_info", "root");

            // CONTRACT: process_info contains: pid, name, port
            AssertFieldExists(processInfo, "pid", "process_info");
            AssertFieldExists(processInfo, "name", "process_info");
            AssertFieldExists(processInfo, "port", "process_info");

            AssertIntegerValue(processInfo, "pid", 12345);  // From TestReportBuilder default
            AssertStringValue(processInfo, "name", "dotnet9ReferenceService");

            // --- SAMPLES ARRAY ---
            AssertArrayCount(root, "samples", 5);
            var samples = root.GetProperty("samples");
            var firstSample = samples[0];

            // CONTRACT: Process samples have specific field names
            AssertFieldExists(firstSample, "timestamp", "sample[0]");
            AssertFieldExists(firstSample, "cpu_percent", "sample[0]");
            AssertFieldExists(firstSample, "memory_rss_mb", "sample[0]");  // RSS, NOT plain memory_mb
            AssertFieldExists(firstSample, "threads", "sample[0]");

            // IMPORTANT CONTRACT: Process metrics use memory_rss_mb (NOT memory_mb)
            AssertFieldNotExists(firstSample, "memory_mb", "sample[0]");

            // Verify sample values
            AssertNumericValue(firstSample, "cpu_percent", 0.0, 2);
            AssertNumericValue(firstSample, "memory_rss_mb", 120.86, 2);
            AssertIntegerValue(firstSample, "threads", 35);

            // CONTRACT: Timestamp format should be ISO8601 without timezone
            AssertTimestampFormat(firstSample, "timestamp", TimestampFormat.Iso8601WithMicroseconds);

            // --- SUMMARY OBJECT ---
            var summary = AssertObjectExists(root, "summary");

            // CONTRACT: Summary field names must match Python exactly
            AssertFieldExists(summary, "avg_cpu_percent");
            AssertFieldExists(summary, "peak_cpu_percent");
            AssertFieldExists(summary, "avg_memory_rss_mb");  // RSS, NOT plain memory_mb
            AssertFieldExists(summary, "peak_memory_rss_mb");  // RSS, NOT plain memory_mb
            AssertFieldExists(summary, "total_samples");

            // IMPORTANT CONTRACT: Summary uses memory_rss_mb (NOT memory_mb)
            AssertFieldNotExists(summary, "avg_memory_mb");
            AssertFieldNotExists(summary, "peak_memory_mb");

            // Verify calculations
            // Avg CPU: (0.0 + 69.9 + 121.7 + 93.9 + 95.9) / 5 = 76.28
            var avgCpu = summary.GetProperty("avg_cpu_percent").GetDouble();
            Assert.True(Math.Abs(avgCpu - 76.28) < 0.1, $"Avg CPU should be ~76.28 but was {avgCpu}");

            // Peak CPU: max(0.0, 69.9, 121.7, 93.9, 95.9) = 121.7
            AssertNumericValue(summary, "peak_cpu_percent", 121.7, 2);

            // Peak Memory: max(120.86, 124.11, 128.98, 133.48, 135.11) = 135.11
            AssertNumericValue(summary, "peak_memory_rss_mb", 135.11, 2);

            // Total samples: 5
            AssertIntegerValue(summary, "total_samples", 5);

            Output.WriteLine("✓ Resource metrics contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: process_info must have correct field order matching Python.
    /// </summary>
    [Fact]
    public async Task ResourceMetrics_ProcessInfo_ShouldHaveCorrectFieldOrder()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var processSamples = ResourceSampleBuilder.CreateProcessSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithProcessResourceSamples(processSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.resource-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract process_info section
            var piStart = rawJson.IndexOf("\"process_info\"");
            var piEnd = rawJson.IndexOf("}", piStart) + 1;
            var piSection = rawJson.Substring(piStart, piEnd - piStart);

            // CONTRACT: Python process_info field order: pid, name, port
            AssertFieldOrder(piSection, "pid", "name", "port");

            Output.WriteLine("✓ process_info field order verified");
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
    public async Task ResourceMetrics_SampleFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var processSamples = ResourceSampleBuilder.CreateProcessSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithProcessResourceSamples(processSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.resource-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract a sample section
            var sampleStart = rawJson.IndexOf("\"timestamp\"");
            var sampleEnd = rawJson.IndexOf("}", sampleStart) + 1;
            var sampleSection = rawJson.Substring(sampleStart - 5, sampleEnd - sampleStart + 5);

            // CONTRACT: Python sample field order: timestamp, cpu_percent, memory_rss_mb, threads
            AssertFieldOrder(sampleSection, "timestamp", "cpu_percent", "memory_rss_mb", "threads");

            Output.WriteLine("✓ Sample field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: CPU can exceed 100% (multi-core).
    /// </summary>
    [Fact]
    public async Task ResourceMetrics_CpuPercent_CanExceed100()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create samples with CPU > 100% (multi-core scenario)
        var processSamples = new List<ProcessResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.5), ElapsedSeconds = 0.5, CpuPercent = 150.0, MemoryRssMb = 100.0, Threads = 10 },
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, CpuPercent = 200.0, MemoryRssMb = 100.0, Threads = 10 },
            new() { Timestamp = testDate.AddSeconds(1.5), ElapsedSeconds = 1.5, CpuPercent = 169.8, MemoryRssMb = 100.0, Threads = 10 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithProcessResourceSamples(processSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.resource-metrics.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            // Peak should be 200% (not capped at 100%)
            var peakCpu = summary.GetProperty("peak_cpu_percent").GetDouble();
            Assert.True(peakCpu > 100, $"CPU can exceed 100% (multi-core) but peak was {peakCpu}");
            AssertNumericValue(summary, "peak_cpu_percent", 200.0, 2);

            Output.WriteLine($"✓ CPU > 100% verified: peak was {peakCpu}%");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Key distinction - uses memory_rss_mb, not memory_mb.
    /// </summary>
    [Fact]
    public async Task ResourceMetrics_KeyDistinction_ShouldUseMemoryRssMb()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var processSamples = ResourceSampleBuilder.CreateProcessSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithProcessResourceSamples(processSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.resource-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Process metrics MUST use "memory_rss_mb" terminology
            Assert.Contains("\"memory_rss_mb\"", rawJson);
            Assert.Contains("\"avg_memory_rss_mb\"", rawJson);
            Assert.Contains("\"peak_memory_rss_mb\"", rawJson);

            // Process metrics must NOT use plain "memory_mb" (that's for containers)
            // Note: We need to check context-aware - memory_mb might appear in container metrics
            // But in resource-metrics.json (process), it should only be memory_rss_mb
            Assert.DoesNotContain("\"avg_memory_mb\"", rawJson);
            Assert.DoesNotContain("\"peak_memory_mb\"", rawJson);

            Output.WriteLine("✓ memory_rss_mb terminology verified (not memory_mb)");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: threads field must be present (unique to process metrics).
    /// </summary>
    [Fact]
    public async Task ResourceMetrics_ThreadsField_ShouldBePresent()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var processSamples = ResourceSampleBuilder.CreateProcessSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithProcessResourceSamples(processSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.resource-metrics.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var samples = doc.RootElement.GetProperty("samples");

            // Every sample must have threads field
            foreach (var sample in samples.EnumerateArray())
            {
                AssertFieldExists(sample, "threads", "process sample");
                var threads = sample.GetProperty("threads").GetInt32();
                Assert.True(threads >= 0, $"Threads should be non-negative but was {threads}");
            }

            Output.WriteLine("✓ threads field verified in all samples");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
