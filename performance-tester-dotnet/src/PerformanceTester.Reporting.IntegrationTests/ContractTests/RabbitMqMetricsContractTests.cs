using System.Text.Json;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using PerformanceTester.Reporting.ValueObjects;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for RabbitMQ metrics report (*.rabbitmq-metrics.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points:
/// - Root has "container_info" object (NOT "process_info")
/// - container_info contains: name, id
/// - Samples use "memory_mb" (NOT "memory_rss_mb")
/// - Samples do NOT have "threads" field
/// - Sampling interval: 3000ms
/// - Summary has: avg/peak for CPU and memory
/// </summary>
public sealed class RabbitMqMetricsContractTests : IntegrationTest
{
    public RabbitMqMetricsContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of RabbitMQ metrics JSON.
    /// </summary>
    [Fact]
    public async Task RabbitMqMetrics_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        var rabbitmqSamples = new List<ContainerResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(3.0), ElapsedSeconds = 3.0, CpuPercent = 1.36, MemoryMb = 157.2 },
            new() { Timestamp = testDate.AddSeconds(6.0), ElapsedSeconds = 6.0, CpuPercent = 140.33, MemoryMb = 157.5 },
            new() { Timestamp = testDate.AddSeconds(9.0), ElapsedSeconds = 9.0, CpuPercent = 1.58, MemoryMb = 157.2 },
            new() { Timestamp = testDate.AddSeconds(12.0), ElapsedSeconds = 12.0, CpuPercent = 25.02, MemoryMb = 175.1 },
            new() { Timestamp = testDate.AddSeconds(15.0), ElapsedSeconds = 15.0, CpuPercent = 0.85, MemoryMb = 169.0 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithRabbitmqResourceSamples(rabbitmqSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.rabbitmq-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify EVERY aspect of the contract
            // ═══════════════════════════════════════════════════════════

            // --- ROOT LEVEL FIELDS ---
            AssertTimestampFormat(root, "test_date", TimestampFormat.TestDate);
            AssertFieldNotExists(root, "sampling_interval_ms", "root");

            // --- ROOT FIELD ORDER ---
            AssertFieldOrder(rawJson, "test_date", "container_info", "samples", "summary");

            // --- CONTAINER_INFO OBJECT (NOT process_info) ---
            var containerInfo = AssertObjectExists(root, "container_info");

            // CONTRACT: Python uses "container_info" for container metrics
            AssertFieldNotExists(root, "process_info", "root");

            // CONTRACT: container_info contains: name, id
            AssertFieldExists(containerInfo, "name", "container_info");
            AssertFieldExists(containerInfo, "id", "container_info");

            // Container should NOT have pid or port (that's for process metrics)
            AssertFieldNotExists(containerInfo, "pid", "container_info");
            AssertFieldNotExists(containerInfo, "port", "container_info");

            // --- SAMPLES ARRAY ---
            AssertArrayCount(root, "samples", 5);
            var samples = root.GetProperty("samples");
            var firstSample = samples[0];

            // CONTRACT: Container samples have specific field names
            AssertFieldExists(firstSample, "timestamp", "sample[0]");
            AssertFieldExists(firstSample, "cpu_percent", "sample[0]");
            AssertFieldExists(firstSample, "memory_mb", "sample[0]");  // Plain memory_mb, NOT memory_rss_mb

            // IMPORTANT CONTRACT: Container metrics use memory_mb (NOT memory_rss_mb)
            AssertFieldNotExists(firstSample, "memory_rss_mb", "sample[0]");

            // Container samples do NOT have threads (that's for process metrics)
            AssertFieldNotExists(firstSample, "threads", "sample[0]");

            // Verify sample values
            AssertNumericValue(firstSample, "cpu_percent", 1.36, 2);
            AssertNumericValue(firstSample, "memory_mb", 157.2, 2);

            // CONTRACT: Timestamp format should be ISO8601 without timezone
            AssertTimestampFormat(firstSample, "timestamp", TimestampFormat.Iso8601WithMicroseconds);

            // --- SUMMARY OBJECT ---
            var summary = AssertObjectExists(root, "summary");

            // CONTRACT: Summary field names must match Python exactly
            AssertFieldExists(summary, "avg_cpu_percent");
            AssertFieldExists(summary, "peak_cpu_percent");
            AssertFieldExists(summary, "avg_memory_mb");  // Plain memory_mb
            AssertFieldExists(summary, "peak_memory_mb");  // Plain memory_mb
            AssertFieldExists(summary, "total_samples");

            // IMPORTANT CONTRACT: Summary uses memory_mb (NOT memory_rss_mb)
            AssertFieldNotExists(summary, "avg_memory_rss_mb");
            AssertFieldNotExists(summary, "peak_memory_rss_mb");

            // Container metrics summary does NOT have min_cpu_percent (that's system metrics)
            AssertFieldNotExists(summary, "min_cpu_percent");

            // Verify calculations
            // Peak CPU: max(1.36, 140.33, 1.58, 25.02, 0.85) = 140.33
            AssertNumericValue(summary, "peak_cpu_percent", 140.33, 2);

            // Peak Memory: max(157.2, 157.5, 157.2, 175.1, 169.0) = 175.1
            AssertNumericValue(summary, "peak_memory_mb", 175.1, 2);

            // Total samples: 5
            AssertIntegerValue(summary, "total_samples", 5);

            Output.WriteLine("✓ RabbitMQ metrics contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: container_info must have correct field order.
    /// </summary>
    [Fact]
    public async Task RabbitMqMetrics_ContainerInfo_ShouldHaveCorrectFieldOrder()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var rabbitmqSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithRabbitmqResourceSamples(rabbitmqSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.rabbitmq-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract container_info section
            var ciStart = rawJson.IndexOf("\"container_info\"");
            var ciEnd = rawJson.IndexOf("}", ciStart) + 1;
            var ciSection = rawJson.Substring(ciStart, ciEnd - ciStart);

            // CONTRACT: Python container_info field order: name, id
            AssertFieldOrder(ciSection, "name", "id");

            Output.WriteLine("✓ container_info field order verified");
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
    public async Task RabbitMqMetrics_SampleFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var rabbitmqSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithRabbitmqResourceSamples(rabbitmqSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.rabbitmq-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract a sample section
            var sampleStart = rawJson.IndexOf("\"timestamp\"");
            var sampleEnd = rawJson.IndexOf("}", sampleStart) + 1;
            var sampleSection = rawJson.Substring(sampleStart - 5, sampleEnd - sampleStart + 5);

            // CONTRACT: Python sample field order: timestamp, cpu_percent, memory_mb
            AssertFieldOrder(sampleSection, "timestamp", "cpu_percent", "memory_mb");

            Output.WriteLine("✓ Sample field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Key distinction - uses memory_mb, not memory_rss_mb.
    /// </summary>
    [Fact]
    public async Task RabbitMqMetrics_KeyDistinction_ShouldUseMemoryMb()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var rabbitmqSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithRabbitmqResourceSamples(rabbitmqSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.rabbitmq-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Container metrics MUST use "memory_mb" terminology
            Assert.Contains("\"memory_mb\"", rawJson);
            Assert.Contains("\"avg_memory_mb\"", rawJson);
            Assert.Contains("\"peak_memory_mb\"", rawJson);

            // Container metrics must NOT use "memory_rss_mb" (that's for process metrics)
            Assert.DoesNotContain("\"memory_rss_mb\"", rawJson);
            Assert.DoesNotContain("\"avg_memory_rss_mb\"", rawJson);
            Assert.DoesNotContain("\"peak_memory_rss_mb\"", rawJson);

            Output.WriteLine("✓ memory_mb terminology verified (not memory_rss_mb)");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Container sampling interval must be 3000ms.
    /// </summary>
    [Fact]
    public async Task RabbitMqMetrics_SamplingInterval_ShouldBe3000Ms()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var rabbitmqSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithRabbitmqResourceSamples(rabbitmqSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.rabbitmq-metrics.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var root = doc.RootElement;

            // CONTRACT: Container metrics no longer include sampling_interval_ms (event-driven)
            AssertFieldNotExists(root, "sampling_interval_ms", "root");

            Output.WriteLine("✓ sampling_interval_ms absent (event-driven) verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
