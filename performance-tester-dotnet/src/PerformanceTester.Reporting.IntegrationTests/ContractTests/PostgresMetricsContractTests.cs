using System.Text.Json;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for PostgreSQL metrics report (*.postgres-metrics.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points (identical structure to RabbitMQ):
/// - Root has "container_info" object (NOT "process_info")
/// - container_info contains: name, id
/// - Samples use "memory_mb" (NOT "memory_rss_mb")
/// - Samples do NOT have "threads" field
/// - Sampling interval: 3000ms
/// - Summary has: avg/peak for CPU and memory
/// </summary>
public sealed class PostgresMetricsContractTests : IntegrationTest
{
    public PostgresMetricsContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of PostgreSQL metrics JSON.
    /// </summary>
    [Fact]
    public async Task PostgresMetrics_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        var postgresSamples = new List<ContainerResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(3.0), ElapsedSeconds = 3.0, CpuPercent = 9.56, MemoryMb = 62.09 },
            new() { Timestamp = testDate.AddSeconds(6.0), ElapsedSeconds = 6.0, CpuPercent = 18.12, MemoryMb = 62.34 },
            new() { Timestamp = testDate.AddSeconds(9.0), ElapsedSeconds = 9.0, CpuPercent = 16.74, MemoryMb = 62.38 },
            new() { Timestamp = testDate.AddSeconds(12.0), ElapsedSeconds = 12.0, CpuPercent = 20.15, MemoryMb = 62.12 },
            new() { Timestamp = testDate.AddSeconds(15.0), ElapsedSeconds = 15.0, CpuPercent = 0.03, MemoryMb = 62.10 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithPostgresResourceSamples(postgresSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.postgres-metrics.json").First();
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

            // --- SAMPLES ARRAY ---
            AssertArrayCount(root, "samples", 5);
            var samples = root.GetProperty("samples");
            var firstSample = samples[0];

            // CONTRACT: Container samples have specific field names
            AssertFieldExists(firstSample, "timestamp", "sample[0]");
            AssertFieldExists(firstSample, "cpu_percent", "sample[0]");
            AssertFieldExists(firstSample, "memory_mb", "sample[0]");  // Plain memory_mb

            // IMPORTANT CONTRACT: Container metrics use memory_mb (NOT memory_rss_mb)
            AssertFieldNotExists(firstSample, "memory_rss_mb", "sample[0]");
            AssertFieldNotExists(firstSample, "threads", "sample[0]");

            // Verify sample values
            AssertNumericValue(firstSample, "cpu_percent", 9.56, 2);
            AssertNumericValue(firstSample, "memory_mb", 62.09, 2);

            // --- SUMMARY OBJECT ---
            var summary = AssertObjectExists(root, "summary");

            // CONTRACT: Summary field names must match Python exactly
            AssertFieldExists(summary, "avg_cpu_percent");
            AssertFieldExists(summary, "peak_cpu_percent");
            AssertFieldExists(summary, "avg_memory_mb");
            AssertFieldExists(summary, "peak_memory_mb");
            AssertFieldExists(summary, "total_samples");

            // Verify calculations
            // Peak CPU: max(9.56, 18.12, 16.74, 20.15, 0.03) = 20.15
            AssertNumericValue(summary, "peak_cpu_percent", 20.15, 2);

            // Peak Memory: max(62.09, 62.34, 62.38, 62.12, 62.10) = 62.38
            AssertNumericValue(summary, "peak_memory_mb", 62.38, 2);

            // Total samples: 5
            AssertIntegerValue(summary, "total_samples", 5);

            Output.WriteLine("✓ PostgreSQL metrics contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: PostgreSQL structure is identical to RabbitMQ.
    /// </summary>
    [Fact]
    public async Task PostgresMetrics_ShouldHaveIdenticalStructureToRabbitMq()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var containerSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithRabbitmqResourceSamples(containerSamples)
            .WithPostgresResourceSamples(containerSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var rabbitmqPath = Directory.GetFiles(outputDirectory, "*.rabbitmq-metrics.json").First();
            var postgresPath = Directory.GetFiles(outputDirectory, "*.postgres-metrics.json").First();

            var rabbitmqDoc = JsonDocument.Parse(await File.ReadAllTextAsync(rabbitmqPath));
            var postgresDoc = JsonDocument.Parse(await File.ReadAllTextAsync(postgresPath));

            var rabbitmqRoot = rabbitmqDoc.RootElement;
            var postgresRoot = postgresDoc.RootElement;

            // Both should have same root-level fields
            AssertFieldExists(rabbitmqRoot, "test_date");
            AssertFieldExists(postgresRoot, "test_date");

            AssertFieldExists(rabbitmqRoot, "container_info");
            AssertFieldExists(postgresRoot, "container_info");

            AssertFieldNotExists(rabbitmqRoot, "sampling_interval_ms", "rabbitmq root");
            AssertFieldNotExists(postgresRoot, "sampling_interval_ms", "postgres root");

            AssertFieldExists(rabbitmqRoot, "samples");
            AssertFieldExists(postgresRoot, "samples");

            AssertFieldExists(rabbitmqRoot, "summary");
            AssertFieldExists(postgresRoot, "summary");

            Output.WriteLine("✓ PostgreSQL and RabbitMQ have identical structure");
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
    public async Task PostgresMetrics_ContainerInfo_ShouldHaveCorrectFieldOrder()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var postgresSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithPostgresResourceSamples(postgresSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.postgres-metrics.json").First();
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
    /// Contract test: Sample field order must match Python.
    /// </summary>
    [Fact]
    public async Task PostgresMetrics_SampleFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var postgresSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithPostgresResourceSamples(postgresSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.postgres-metrics.json").First();
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
    /// Contract test: Summary field order must match Python.
    /// </summary>
    [Fact]
    public async Task PostgresMetrics_SummaryFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var postgresSamples = ResourceSampleBuilder.CreateContainerSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithPostgresResourceSamples(postgresSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.postgres-metrics.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract summary section
            var summaryStart = rawJson.IndexOf("\"summary\"");
            var summarySection = rawJson.Substring(summaryStart);

            // CONTRACT: Python summary field order
            AssertFieldOrder(summarySection,
                "avg_cpu_percent",
                "peak_cpu_percent",
                "avg_memory_mb",
                "peak_memory_mb",
                "total_samples");

            Output.WriteLine("✓ Summary field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
