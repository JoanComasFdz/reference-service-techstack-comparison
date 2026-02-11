using System.Text.Json;
using PerformanceTester.Reporting.ValueObjects;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for events throughput report (*.events-throughput.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points:
/// - Sample fields: timestamp, elapsed_seconds, total_events, events_per_second
/// - Summary fields: avg_events_per_second, peak_events_per_second, min_events_per_second,
///                   std_dev_events_per_second, cv_events_per_second, avg_response_time_ms,
///                   total_samples, total_events
/// - Min calculation EXCLUDES zeros
/// - Average calculation INCLUDES zeros
/// - Timestamp format: ISO8601 without timezone (yyyy-MM-ddTHH:mm:ss.ffffff)
/// </summary>
public sealed class EventsThroughputContractTests : IntegrationTest
{
    public EventsThroughputContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of events throughput JSON.
    /// </summary>
    [Fact]
    public async Task EventsThroughput_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create samples with known values for calculation verification
        var eventsSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.1), ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = testDate.AddSeconds(0.2), ElapsedSeconds = 0.2, Rate = 200.0, CumulativeCount = 30 },
            new() { Timestamp = testDate.AddSeconds(0.3), ElapsedSeconds = 0.3, Rate = 150.0, CumulativeCount = 45 },
            new() { Timestamp = testDate.AddSeconds(0.4), ElapsedSeconds = 0.4, Rate = 50.0, CumulativeCount = 50 }
        };
        // Expected: Avg=125.00, Peak=200.00, Min=50.00, Total=50

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify EVERY aspect of the contract
            // ═══════════════════════════════════════════════════════════

            // --- ROOT LEVEL FIELDS ---
            AssertTimestampFormat(root, "test_date", TimestampFormat.TestDate);
            AssertStringValue(root, "test_date", "2025-11-13 14:25:30");

            AssertIntegerValue(root, "sampling_interval_ms", 100);

            // --- ROOT FIELD ORDER ---
            AssertFieldOrder(rawJson, "test_date", "sampling_interval_ms", "samples", "summary");

            // --- SAMPLES ARRAY ---
            AssertArrayCount(root, "samples", 4);
            var samples = root.GetProperty("samples");
            var firstSample = samples[0];

            // CONTRACT: Sample fields must be named as Python produces them
            // Python uses: timestamp, elapsed_seconds, total_events, events_per_second
            AssertFieldExists(firstSample, "timestamp", "sample[0]");
            AssertFieldExists(firstSample, "elapsed_seconds", "sample[0]");
            AssertFieldExists(firstSample, "total_events", "sample[0]");
            AssertFieldExists(firstSample, "events_per_second", "sample[0]");

            // Verify camelCase alternatives are NOT present
            AssertFieldNotExists(firstSample, "elapsedSeconds", "sample[0]");
            AssertFieldNotExists(firstSample, "totalEvents", "sample[0]");
            AssertFieldNotExists(firstSample, "eventsPerSecond", "sample[0]");

            // IMPORTANT CONTRACT: Python uses these EXACT field names, NOT throughput_rate/cumulative_count
            AssertFieldNotExists(firstSample, "throughput_rate", "sample[0]");
            AssertFieldNotExists(firstSample, "cumulative_count", "sample[0]");

            // Verify sample values
            AssertNumericValue(firstSample, "elapsed_seconds", 0.1, 3);
            AssertNumericValue(firstSample, "events_per_second", 100.0, 2);
            AssertIntegerValue(firstSample, "total_events", 10);

            // CONTRACT: Timestamp format should be ISO8601 without timezone offset
            // Python produces: "2026-01-22T11:41:55.150402" (no +00:00)
            AssertTimestampFormat(firstSample, "timestamp", TimestampFormat.Iso8601WithMicroseconds);

            // --- SUMMARY OBJECT ---
            var summary = AssertObjectExists(root, "summary");

            // CONTRACT: Summary field names must match Python exactly
            AssertFieldExists(summary, "avg_events_per_second");
            AssertFieldExists(summary, "peak_events_per_second");
            AssertFieldExists(summary, "min_events_per_second");
            AssertFieldExists(summary, "std_dev_events_per_second");
            AssertFieldExists(summary, "cv_events_per_second");
            AssertFieldExists(summary, "avg_response_time_ms");
            AssertFieldExists(summary, "total_samples");
            AssertFieldExists(summary, "total_events");

            // Verify camelCase alternatives are NOT present
            AssertFieldNotExists(summary, "avgEventsPerSecond");
            AssertFieldNotExists(summary, "avgRate");
            AssertFieldNotExists(summary, "peakRate");

            // IMPORTANT CONTRACT: Python uses *_events_per_second, NOT *_rate
            AssertFieldNotExists(summary, "avg_rate");
            AssertFieldNotExists(summary, "peak_rate");
            AssertFieldNotExists(summary, "min_rate");
            AssertFieldNotExists(summary, "std_dev_rate");
            AssertFieldNotExists(summary, "cv_rate");
            AssertFieldNotExists(summary, "total_count");

            // --- SUMMARY CALCULATIONS ---
            // Avg: (100 + 200 + 150 + 50) / 4 = 125.00
            AssertNumericValue(summary, "avg_events_per_second", 125.00, 2);

            // Peak: max(100, 200, 150, 50) = 200.00
            AssertNumericValue(summary, "peak_events_per_second", 200.00, 2);

            // Min: min(100, 200, 150, 50) = 50.00 (no zeros to exclude)
            AssertNumericValue(summary, "min_events_per_second", 50.00, 2);

            // Total samples: 4
            AssertIntegerValue(summary, "total_samples", 4);

            // Total events: last cumulative count = 50
            AssertIntegerValue(summary, "total_events", 50);

            // Response time: 1000.0 / 125.0 = 8.000
            AssertNumericValue(summary, "avg_response_time_ms", 8.000, 3);

            Output.WriteLine("✓ Events throughput contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Min calculation must exclude zeros.
    /// This is a CRITICAL contract point that differs from naive implementation.
    /// </summary>
    [Fact]
    public async Task EventsThroughput_MinCalculation_ShouldExcludeZeros()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create samples with zeros - min should exclude them
        var eventsSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.1), ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = testDate.AddSeconds(0.2), ElapsedSeconds = 0.2, Rate = 0.0, CumulativeCount = 10 }, // Zero
            new() { Timestamp = testDate.AddSeconds(0.3), ElapsedSeconds = 0.3, Rate = 50.0, CumulativeCount = 15 },
            new() { Timestamp = testDate.AddSeconds(0.4), ElapsedSeconds = 0.4, Rate = 0.0, CumulativeCount = 15 }, // Zero
            new() { Timestamp = testDate.AddSeconds(0.5), ElapsedSeconds = 0.5, Rate = 75.0, CumulativeCount = 22 }
        };
        // Non-zero values: 100, 50, 75 → Min should be 50 (NOT 0)
        // Avg should include zeros: (100 + 0 + 50 + 0 + 75) / 5 = 45.0

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            // CONTRACT: Min excludes zeros
            var minRate = summary.GetProperty("min_events_per_second").GetDouble();
            Assert.True(minRate > 0, $"Min should exclude zeros but was {minRate}");
            AssertNumericValue(summary, "min_events_per_second", 50.0, 2);

            // CONTRACT: Average includes zeros
            // (100 + 0 + 50 + 0 + 75) / 5 = 45.0
            AssertNumericValue(summary, "avg_events_per_second", 45.0, 2);

            Output.WriteLine("✓ Min excludes zeros contract verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Standard deviation must use N-1 denominator (sample std dev).
    /// </summary>
    [Fact]
    public async Task EventsThroughput_StdDev_ShouldUseSampleStdDev()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create samples with known values for std dev calculation
        // Values: 100, 200
        // Mean: 150
        // Sample variance: ((100-150)^2 + (200-150)^2) / (2-1) = (2500 + 2500) / 1 = 5000
        // Sample std dev: sqrt(5000) ≈ 70.71
        var eventsSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.1), ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = testDate.AddSeconds(0.2), ElapsedSeconds = 0.2, Rate = 200.0, CumulativeCount = 30 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            // Expected sample std dev: sqrt(5000) ≈ 70.71
            var stdDev = summary.GetProperty("std_dev_events_per_second").GetDouble();
            Assert.True(Math.Abs(stdDev - 70.71) < 0.1,
                $"Std dev should be ~70.71 (sample std dev with N-1) but was {stdDev}");

            Output.WriteLine($"✓ Sample std dev verified: {stdDev}");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: CV% calculation must be (std_dev / mean) * 100.
    /// </summary>
    [Fact]
    public async Task EventsThroughput_CvPercent_ShouldBeCalculatedCorrectly()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Values: 100, 200 → Mean: 150, Std Dev: 70.71
        // CV% = (70.71 / 150) * 100 = 47.14%
        var eventsSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.1), ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = testDate.AddSeconds(0.2), ElapsedSeconds = 0.2, Rate = 200.0, CumulativeCount = 30 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            // CV% = (std_dev / mean) * 100
            var avg = summary.GetProperty("avg_events_per_second").GetDouble();
            var stdDev = summary.GetProperty("std_dev_events_per_second").GetDouble();
            var cv = summary.GetProperty("cv_events_per_second").GetDouble();

            var expectedCv = (stdDev / avg) * 100;
            Assert.True(Math.Abs(cv - expectedCv) < 0.1,
                $"CV% should be {expectedCv:F2} but was {cv:F2}");

            Output.WriteLine($"✓ CV% calculation verified: {cv:F2}%");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Response time must be 1000.0 / avg_throughput.
    /// </summary>
    [Fact]
    public async Task EventsThroughput_ResponseTime_ShouldBeInverseOfThroughput()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Values: 100, 200, 200, 100 → Mean: 150
        // Response time = 1000.0 / 150 = 6.667 ms
        var eventsSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.1), ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = testDate.AddSeconds(0.2), ElapsedSeconds = 0.2, Rate = 200.0, CumulativeCount = 30 },
            new() { Timestamp = testDate.AddSeconds(0.3), ElapsedSeconds = 0.3, Rate = 200.0, CumulativeCount = 50 },
            new() { Timestamp = testDate.AddSeconds(0.4), ElapsedSeconds = 0.4, Rate = 100.0, CumulativeCount = 60 }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var summary = doc.RootElement.GetProperty("summary");

            var avg = summary.GetProperty("avg_events_per_second").GetDouble();
            var responseTime = summary.GetProperty("avg_response_time_ms").GetDouble();

            var expectedResponseTime = 1000.0 / avg;
            Assert.True(Math.Abs(responseTime - expectedResponseTime) < 0.01,
                $"Response time should be {expectedResponseTime:F3} but was {responseTime:F3}");

            Output.WriteLine($"✓ Response time calculation verified: {responseTime:F3} ms");
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
    public async Task EventsThroughput_SampleFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var eventsSamples = ThroughputMetricSampleBuilder.CreateSimple(testDate, 3);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract a sample section for field order verification
            var sampleStart = rawJson.IndexOf("\"timestamp\"");
            var sampleEnd = rawJson.IndexOf("}", sampleStart) + 1;
            var sampleSection = rawJson.Substring(sampleStart - 5, sampleEnd - sampleStart + 5);

            // CONTRACT: Python sample field order: timestamp, elapsed_seconds, total_events, events_per_second
            AssertFieldOrder(sampleSection, "timestamp", "elapsed_seconds", "total_events", "events_per_second");

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
    public async Task EventsThroughput_SummaryFieldOrder_ShouldMatchPython()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var eventsSamples = ThroughputMetricSampleBuilder.CreateSimple(testDate, 5);

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("testService")
            .WithEventsThroughputSamples(eventsSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.events-throughput.json").First();
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract summary section
            var summaryStart = rawJson.IndexOf("\"summary\"");
            var summarySection = rawJson.Substring(summaryStart);

            // CONTRACT: Python summary field order
            AssertFieldOrder(summarySection,
                "avg_events_per_second",
                "peak_events_per_second",
                "min_events_per_second",
                "std_dev_events_per_second",
                "cv_events_per_second",
                "avg_response_time_ms",
                "total_samples",
                "total_events");

            Output.WriteLine("✓ Summary field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
