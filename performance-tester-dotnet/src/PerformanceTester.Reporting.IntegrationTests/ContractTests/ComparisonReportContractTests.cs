using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.ValueObjects;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for comparison report (*.md).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Key contract points:
/// - Header: "# Test Results Comparison Report" followed by Generated timestamp
/// - Sections: Test Environment, Test Runs Overview, Throughput Comparison, Resource Usage, Performance Highlights
/// - Tables: Sorted by specific metrics with medal emojis (🥇🥈🥉)
/// - Medal assignment: Top 3 distinct values, ties get same medal
/// - Sort directions: Higher is better for throughput, lower for resource usage
/// </summary>
public sealed class ComparisonReportContractTests : IntegrationTest
{
    public ComparisonReportContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying markdown comparison report structure.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create multiple test reports for comparison
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create 3 test reports with different performance characteristics
        var report1 = CreateTestReportWithMetrics(testDate, "serviceA",
            eventsThroughput: 300.0, apiThroughput: 1000.0,
            avgCpu: 30.0, avgMemory: 100.0);
        var report2 = CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB",
            eventsThroughput: 250.0, apiThroughput: 800.0,
            avgCpu: 40.0, avgMemory: 150.0);
        var report3 = CreateTestReportWithMetrics(testDate.AddSeconds(120), "serviceC",
            eventsThroughput: 200.0, apiThroughput: 600.0,
            avgCpu: 50.0, avgMemory: 200.0);

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate comparison report
            // ═══════════════════════════════════════════════════════════
            var reports = new List<TestReport> { report1, report2, report3 };
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify structure and content
            // ═══════════════════════════════════════════════════════════

            // --- HEADER ---
            AssertLineExists(markdown, "# Test Results Comparison Report");
            AssertContainsPattern(markdown, @"\*\*Generated:\*\* \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");

            // --- SECTION HEADERS ---
            AssertSectionExists(markdown, "## Test Environment");
            AssertSectionExists(markdown, "## Test Runs Overview");
            AssertSectionExists(markdown, "## Throughput Comparison");
            AssertSectionExists(markdown, "## Resource Usage Comparison");
            AssertSectionExists(markdown, "## Performance Highlights");

            // --- SUBSECTION HEADERS ---
            AssertSectionExists(markdown, "### Event Processing Throughput");
            AssertSectionExists(markdown, "### API Throughput");
            AssertSectionExists(markdown, "### Process CPU Usage");
            AssertSectionExists(markdown, "### Process Memory Usage");
            AssertSectionExists(markdown, "### System-Wide Metrics");

            Output.WriteLine("✓ Comparison report contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Header format must match Python.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_Header_ShouldMatchPythonFormat()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);
            var lines = markdown.Split('\n');

            // CONTRACT: First line must be the title
            Assert.Equal("# Test Results Comparison Report", lines[0].Trim());

            // CONTRACT: Second line is empty
            Assert.Empty(lines[1].Trim());

            // CONTRACT: Third line has Generated timestamp
            Assert.StartsWith("**Generated:**", lines[2].Trim());
            AssertContainsPattern(lines[2], @"\*\*Generated:\*\* \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");

            Output.WriteLine("✓ Header format verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Test Environment section format.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_TestEnvironment_ShouldHaveRequiredFields()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Test Environment section has hardware info
            AssertSectionExists(markdown, "## Test Environment");

            // CONTRACT: CPU information
            AssertContainsPattern(markdown, @"\*\*CPU Model:\*\*");

            // CONTRACT: Memory information
            AssertContainsPattern(markdown, @"(Total Memory|RAM):");

            // CONTRACT: Platform information
            AssertContainsPattern(markdown, @"\*\*Platform:\*\*");

            Output.WriteLine("✓ Test Environment section verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Test Runs Overview table columns.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_TestRunsOverview_ShouldHaveCorrectColumns()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Table columns for Test Runs Overview
            // | # | Test Date | Process | Runtime (s) | Events | API Duration | Workers |
            AssertTableHasColumn(markdown, "Test Runs Overview", "#");
            AssertTableHasColumn(markdown, "Test Runs Overview", "Test Date");
            AssertTableHasColumn(markdown, "Test Runs Overview", "Process");
            AssertTableHasColumn(markdown, "Test Runs Overview", "Runtime");

            Output.WriteLine("✓ Test Runs Overview columns verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Event Processing Throughput table columns.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_EventThroughputTable_ShouldHaveCorrectColumns()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Table columns for Event Processing Throughput
            // | # | Process | Avg (events/s) | Peak (events/s) | Min (events/s)* | Std Dev** | CV%*** |
            AssertTableHasColumn(markdown, "Event Processing Throughput", "Process");
            AssertTableHasColumn(markdown, "Event Processing Throughput", "Avg");
            AssertTableHasColumn(markdown, "Event Processing Throughput", "Peak");
            AssertTableHasColumn(markdown, "Event Processing Throughput", "Min");
            AssertTableHasColumn(markdown, "Event Processing Throughput", "Std Dev");
            AssertTableHasColumn(markdown, "Event Processing Throughput", "CV%");

            Output.WriteLine("✓ Event Processing Throughput columns verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: API Throughput table columns.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_ApiThroughputTable_ShouldHaveCorrectColumns()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Table columns for API Throughput
            // | # | Process | Total Requests | Avg (calls/s) | Peak (calls/s) | Min (calls/s)* | Std Dev** | CV%*** | Avg Response Time (ms) |
            AssertTableHasColumn(markdown, "API Throughput", "Process");
            AssertTableHasColumn(markdown, "API Throughput", "Avg");
            AssertTableHasColumn(markdown, "API Throughput", "Peak");
            AssertTableHasColumn(markdown, "API Throughput", "Min");
            AssertTableHasColumn(markdown, "API Throughput", "Std Dev");
            AssertTableHasColumn(markdown, "API Throughput", "CV%");
            AssertTableHasColumn(markdown, "API Throughput", "Response Time");

            Output.WriteLine("✓ API Throughput columns verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Process CPU Usage table columns.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_ProcessCpuTable_ShouldHaveCorrectColumns()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Table columns for Process CPU Usage
            // | # | Process | Avg CPU % | Peak CPU % |
            AssertTableHasColumn(markdown, "Process CPU Usage", "Process");
            AssertTableHasColumn(markdown, "Process CPU Usage", "Avg CPU");
            AssertTableHasColumn(markdown, "Process CPU Usage", "Peak CPU");

            Output.WriteLine("✓ Process CPU Usage columns verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Process Memory Usage table columns.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_ProcessMemoryTable_ShouldHaveCorrectColumns()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Table columns for Process Memory Usage
            // | # | Process | Avg Memory (MB) | Peak Memory (MB) |
            AssertTableHasColumn(markdown, "Process Memory Usage", "Process");
            AssertTableHasColumn(markdown, "Process Memory Usage", "Avg Memory");
            AssertTableHasColumn(markdown, "Process Memory Usage", "Peak Memory");

            Output.WriteLine("✓ Process Memory Usage columns verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Medal emojis are present for best performers.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_MedalAssignment_ShouldUseMedalEmojis()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        // Create 3 reports with distinct performance to ensure medals
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(120), "serviceC", 200.0, 600.0, 50.0, 200.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Medal emojis should be present
            Assert.Contains("🥇", markdown);  // Gold medal for best
            Assert.Contains("🥈", markdown);  // Silver medal for second best
            Assert.Contains("🥉", markdown);  // Bronze medal for third best

            Output.WriteLine("✓ Medal emojis verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Performance Highlights section format.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_PerformanceHighlights_ShouldHaveRequiredEntries()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Performance Highlights section has specific entries
            AssertSectionExists(markdown, "## Performance Highlights");

            // CONTRACT: Required highlight entries
            AssertContainsPattern(markdown, @"Fastest Event Processing");
            AssertContainsPattern(markdown, @"Fastest API Throughput");
            AssertContainsPattern(markdown, @"Most Stable.*Event Processing");
            AssertContainsPattern(markdown, @"Most Stable.*API Throughput");
            AssertContainsPattern(markdown, @"Lowest.*CPU");
            AssertContainsPattern(markdown, @"Lowest.*Memory");

            Output.WriteLine("✓ Performance Highlights entries verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Footnotes for statistical abbreviations.
    /// </summary>
    [Fact]
    public async Task ComparisonReport_Footnotes_ShouldExplainStatisticalTerms()
    {
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var reports = new List<TestReport>
        {
            CreateTestReportWithMetrics(testDate, "serviceA", 300.0, 1000.0, 30.0, 100.0),
            CreateTestReportWithMetrics(testDate.AddSeconds(60), "serviceB", 250.0, 800.0, 40.0, 150.0)
        };

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            var outputFilePath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
                ResultsSourceFolder.FromString(outputDirectory), reports);

            var markdown = await File.ReadAllTextAsync(outputFilePath);

            // CONTRACT: Footnotes explain asterisk markers
            // *Min excludes zero values
            // **Std Dev explanation
            // ***CV% explanation
            AssertContainsPattern(markdown, @"\*Min.*zero");
            AssertContainsPattern(markdown, @"\*\*Std Dev|Standard deviation");
            AssertContainsPattern(markdown, @"\*\*\*CV%|Coefficient of variation");

            Output.WriteLine("✓ Footnotes verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private static TestReport CreateTestReportWithMetrics(
        DateTime testDate,
        string processName,
        double eventsThroughput,
        double apiThroughput,
        double avgCpu,
        double avgMemory)
    {
        var eventsSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, Rate = eventsThroughput * 0.8, CumulativeCount = (int)(eventsThroughput * 0.8) },
            new() { Timestamp = testDate.AddSeconds(2.0), ElapsedSeconds = 2.0, Rate = eventsThroughput, CumulativeCount = (int)(eventsThroughput * 1.8) },
            new() { Timestamp = testDate.AddSeconds(3.0), ElapsedSeconds = 3.0, Rate = eventsThroughput * 1.2, CumulativeCount = (int)(eventsThroughput * 3.0) }
        };

        var apiSamples = new List<ThroughputMetricSample>
        {
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, Rate = apiThroughput * 0.9, CumulativeCount = (int)(apiThroughput * 0.9) },
            new() { Timestamp = testDate.AddSeconds(2.0), ElapsedSeconds = 2.0, Rate = apiThroughput, CumulativeCount = (int)(apiThroughput * 1.9) },
            new() { Timestamp = testDate.AddSeconds(3.0), ElapsedSeconds = 3.0, Rate = apiThroughput * 1.1, CumulativeCount = (int)(apiThroughput * 3.0) }
        };

        var processSamples = new List<ProcessResourceSample>
        {
            new() { Timestamp = testDate.AddSeconds(0.5), ElapsedSeconds = 0.5, CpuPercent = avgCpu * 0.9, MemoryRssMb = avgMemory * 0.9, Threads = 35 },
            new() { Timestamp = testDate.AddSeconds(1.0), ElapsedSeconds = 1.0, CpuPercent = avgCpu, MemoryRssMb = avgMemory, Threads = 35 },
            new() { Timestamp = testDate.AddSeconds(1.5), ElapsedSeconds = 1.5, CpuPercent = avgCpu * 1.1, MemoryRssMb = avgMemory * 1.1, Threads = 35 }
        };

        var systemSamples = ResourceSampleBuilder.CreateSystem(testDate, 3);

        return new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName(processName)
            .WithEventsThroughputSamples(eventsSamples)
            .WithApiThroughputSamples(apiSamples)
            .WithProcessResourceSamples(processSamples)
            .WithSystemResourceSamples(systemSamples)
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();
    }

    private static void AssertLineExists(string markdown, string expectedLine)
    {
        Assert.Contains(expectedLine, markdown);
    }

    private static void AssertContainsPattern(string text, string pattern)
    {
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(text, pattern, global::System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            $"Expected pattern '{pattern}' not found in text");
    }

    private static void AssertSectionExists(string markdown, string sectionHeader)
    {
        Assert.Contains(sectionHeader, markdown);
    }

    private static void AssertTableHasColumn(string markdown, string sectionName, string columnName)
    {
        // Find the section
        var sectionIndex = markdown.IndexOf(sectionName, StringComparison.OrdinalIgnoreCase);
        Assert.True(sectionIndex >= 0, $"Section '{sectionName}' not found");

        // Find the next table header row (starts with |)
        var afterSection = markdown.Substring(sectionIndex);
        var tableStart = afterSection.IndexOf("\n|");
        Assert.True(tableStart >= 0, $"No table found after section '{sectionName}'");

        var tableEnd = afterSection.IndexOf("\n\n", tableStart);
        if (tableEnd < 0) tableEnd = afterSection.Length;

        var tableContent = afterSection.Substring(tableStart, tableEnd - tableStart);

        Assert.True(
            tableContent.Contains(columnName, StringComparison.OrdinalIgnoreCase),
            $"Column '{columnName}' not found in table under section '{sectionName}'");
    }
}
