using System.Text.Json;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for API throughput report (*.api-throughput.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points:
/// - Sample fields: timestamp, elapsed_seconds, total_calls, calls_per_second (NOT events)
/// - Summary fields: avg_calls_per_second, peak_calls_per_second, min_calls_per_second,
///                   std_dev_calls_per_second, cv_calls_per_second, avg_response_time_ms,
///                   total_samples, total_calls
/// - Same statistical rules as events: min excludes zeros, avg includes zeros
/// </summary>
public sealed class ApiThroughputContractTests : IntegrationTest
{
    public ApiThroughputContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of API throughput JSON.
    /// </summary>
    [Fact]
    public async Task ApiThroughput_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create samples with known values
        var apiSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, Rate = 794.0, CumulativeCount = 794 },
            new() { Timestamp = testDate.AddSeconds(2.0), ElapsedSeconds = 2.0, Rate = 774.0, CumulativeCount = 1568 },
            new() { Timestamp = testDate.AddSeconds(3.0), ElapsedSeconds = 3.0, Rate = 892.0, CumulativeCount = 2460 },
            new() { Timestamp = testDate.AddSeconds(4.0), ElapsedSeconds = 4.0, Rate = 822.0, CumulativeCount = 3282 },
            new() { Timestamp = testDate.AddSeconds(5.0), ElapsedSeconds = 5.0, Rate = 856.0, CumulativeCount = 4138 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithApiThroughputSamples(apiSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.api-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify EVERY aspect of the contract
            // ═══════════════════════════════════════════════════════════

            // --- ROOT LEVEL FIELDS ---
            AssertTimestampFormat(root, "test_date", TimestampFormat.TestDate);
            AssertIntegerValue(root, "sampling_interval_ms", 100);

            // --- ROOT FIELD ORDER ---
            AssertFieldOrder(rawJson, "test_date", "sampling_interval_ms", "samples", "summary");

            // --- SAMPLES ARRAY ---
            AssertArrayCount(root, "samples", 5);
            var samples = root.GetProperty("samples");
            var firstSample = samples[0];

            // CONTRACT: API throughput uses "total_calls" and "calls_per_second" (NOT events)
            AssertFieldExists(firstSample, "timestamp", "sample[0]");
            AssertFieldExists(firstSample, "elapsed_seconds", "sample[0]");
            AssertFieldExists(firstSample, "total_calls", "sample[0]");
            AssertFieldExists(firstSample, "calls_per_second", "sample[0]");

            // Verify events fields are NOT present (API uses calls, not events)
            AssertFieldNotExists(firstSample, "total_events", "sample[0]");
            AssertFieldNotExists(firstSample, "events_per_second", "sample[0]");

            // IMPORTANT CONTRACT: Python uses calls, NOT throughput_rate/cumulative_count
            AssertFieldNotExists(firstSample, "throughput_rate", "sample[0]");
            AssertFieldNotExists(firstSample, "cumulative_count", "sample[0]");

            // Verify sample values
            AssertNumericValue(firstSample, "elapsed_seconds", 1.0, 3);
            AssertNumericValue(firstSample, "calls_per_second", 794.0, 2);
            AssertIntegerValue(firstSample, "total_calls", 794);

            // CONTRACT: Timestamp format should be ISO8601 without timezone offset
            AssertTimestampFormat(firstSample, "timestamp", TimestampFormat.Iso8601WithMicroseconds);

            // --- SUMMARY OBJECT ---
            var summary = AssertObjectExists(root, "summary");

            // CONTRACT: Summary field names must use "calls" (NOT events, NOT rate)
            AssertFieldExists(summary, "avg_calls_per_second");
            AssertFieldExists(summary, "peak_calls_per_second");
            AssertFieldExists(summary, "min_calls_per_second");
            AssertFieldExists(summary, "std_dev_calls_per_second");
            AssertFieldExists(summary, "cv_calls_per_second");
            AssertFieldExists(summary, "avg_response_time_ms");
            AssertFieldExists(summary, "total_samples");
            AssertFieldExists(summary, "total_calls");

            // IMPORTANT CONTRACT: Python uses *_calls_per_second, NOT *_rate or *_events_per_second
            AssertFieldNotExists(summary, "avg_rate");
            AssertFieldNotExists(summary, "peak_rate");
            AssertFieldNotExists(summary, "min_rate");
            AssertFieldNotExists(summary, "avg_events_per_second");
            AssertFieldNotExists(summary, "total_events");
            AssertFieldNotExists(summary, "total_count");

            // --- SUMMARY CALCULATIONS ---
            // Avg: (794 + 774 + 892 + 822 + 856) / 5 = 827.6
            var avg = summary.GetProperty("avg_calls_per_second").GetDouble();
            Assert.True(Math.Abs(avg - 827.6) < 0.1, $"Avg should be ~827.6 but was {avg}");

            // Peak: max(794, 774, 892, 822, 856) = 892.0
            AssertNumericValue(summary, "peak_calls_per_second", 892.0, 2);

            // Min: min(794, 774, 892, 822, 856) = 774.0 (no zeros)
            AssertNumericValue(summary, "min_calls_per_second", 774.0, 2);

            // Total samples: 5
            AssertIntegerValue(summary, "total_samples", 5);

            // Total calls: last cumulative count = 4138
            AssertIntegerValue(summary, "total_calls", 4138);

            Output.WriteLine("✓ API throughput contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: API throughput sample field order must match Python.
    /// </summary>
    [Fact]
    public async Task ApiThroughput_SampleFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var apiSamples = ThroughputMetricSampleBuilder.CreateSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithApiThroughputSamples(apiSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.api-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract a sample section for field order verification
            var sampleStart = rawJson.IndexOf("\"timestamp\"");
            var sampleEnd = rawJson.IndexOf("}", sampleStart) + 1;
            var sampleSection = rawJson.Substring(sampleStart - 5, sampleEnd - sampleStart + 5);

            // CONTRACT: Python sample field order: timestamp, elapsed_seconds, total_calls, calls_per_second
            AssertFieldOrder(sampleSection, "timestamp", "elapsed_seconds", "total_calls", "calls_per_second");

            Output.WriteLine("✓ API sample field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: API throughput summary field order must match Python.
    /// </summary>
    [Fact]
    public async Task ApiThroughput_SummaryFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var apiSamples = ThroughputMetricSampleBuilder.CreateSimple(testDate, 5);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithApiThroughputSamples(apiSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.api-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract summary section
            var summaryStart = rawJson.IndexOf("\"summary\"");
            var summarySection = rawJson.Substring(summaryStart);

            // CONTRACT: Python summary field order (using calls, not events)
            AssertFieldOrder(summarySection,
                "avg_calls_per_second",
                "peak_calls_per_second",
                "min_calls_per_second",
                "std_dev_calls_per_second",
                "cv_calls_per_second",
                "avg_response_time_ms",
                "total_samples",
                "total_calls");

            Output.WriteLine("✓ API summary field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: API throughput with zero values - min should exclude zeros.
    /// </summary>
    [Fact]
    public async Task ApiThroughput_MinCalculation_ShouldExcludeZeros()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create samples with a trailing zero (common in API tests - final measurement)
        var apiSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, Rate = 794.0, CumulativeCount = 794 },
            new() { Timestamp = testDate.AddSeconds(2.0), ElapsedSeconds = 2.0, Rate = 774.0, CumulativeCount = 1568 },
            new() { Timestamp = testDate.AddSeconds(3.0), ElapsedSeconds = 3.0, Rate = 0.0, CumulativeCount = 1568 } // Final zero
        };
        // Min should be 774 (NOT 0)

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithApiThroughputSamples(apiSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.api-throughput.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            // CONTRACT: Min excludes zeros
            var minRate = summary.GetProperty("min_calls_per_second").GetDouble();
            Assert.True(minRate > 0, $"Min should exclude zeros but was {minRate}");
            AssertNumericValue(summary, "min_calls_per_second", 774.0, 2);

            Output.WriteLine("✓ API min excludes zeros contract verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Key difference from events - uses "calls" terminology.
    /// </summary>
    [Fact]
    public async Task ApiThroughput_KeyDifference_ShouldUseCallsTerminology()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var apiSamples = ThroughputMetricSampleBuilder.CreateSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithApiThroughputSamples(apiSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.api-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // API throughput MUST use "calls" terminology
            Assert.Contains("\"total_calls\"", rawJson);
            Assert.Contains("\"calls_per_second\"", rawJson);
            Assert.Contains("\"avg_calls_per_second\"", rawJson);
            Assert.Contains("\"peak_calls_per_second\"", rawJson);
            Assert.Contains("\"min_calls_per_second\"", rawJson);
            Assert.Contains("\"std_dev_calls_per_second\"", rawJson);
            Assert.Contains("\"cv_calls_per_second\"", rawJson);

            // API throughput must NOT use "events" terminology
            Assert.DoesNotContain("\"total_events\"", rawJson);
            Assert.DoesNotContain("\"events_per_second\"", rawJson);
            Assert.DoesNotContain("\"avg_events_per_second\"", rawJson);

            Output.WriteLine("✓ API calls terminology verified (not events)");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
