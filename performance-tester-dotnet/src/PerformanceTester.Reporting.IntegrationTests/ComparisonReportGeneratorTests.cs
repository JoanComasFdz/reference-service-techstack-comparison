using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests;

/// <summary>
/// Integration tests for IComparisonReportGenerator (Markdown comparison reports).
/// Tests verify comparison report generation with rankings and statistics.
/// </summary>
public sealed class ComparisonReportGeneratorTests : IntegrationTest
{
    public ComparisonReportGeneratorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task GenerateComparisonReport_WithTwoReports_CreatesValidMarkdown()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
        var reports = new[]
        {
            new TestReportBuilder()
                .WithProcessName("goReferenceService")
                .WithRuntime(100.5)
                .WithCpu(25.5)
                .WithMemory(150.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("rustReferenceService")
                .WithRuntime(95.2)
                .WithCpu(22.3)
                .WithMemory(120.0)
                .Build()
        };

        try
        {
            // Act
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);

            // Assert
            Assert.True(File.Exists(outputPath), "Comparison report file should be created");

            var markdown = await File.ReadAllTextAsync(outputPath);

            // Verify major sections exist
            Assert.Contains("# Test Results Comparison Report", markdown);
            Assert.Contains("## Test Environment", markdown);
            Assert.Contains("## Test Runs Overview (sorted by Runtime - lower is better)", markdown);
            Assert.Contains("## Throughput Comparison", markdown);
            Assert.Contains("### Event Processing Throughput (sorted by Avg - higher is better)", markdown);
            Assert.Contains("### API Throughput (sorted by Avg - higher is better)", markdown);
            Assert.Contains("## Resource Usage Comparison", markdown);
            Assert.Contains("### Process CPU Usage (sorted by Avg CPU % - lower is better)", markdown);
            Assert.Contains("### Process Memory Usage (sorted by Avg - lower is better)", markdown);
            Assert.Contains("### System-Wide Metrics (sorted by Avg System CPU % - lower is better)", markdown);
            Assert.Contains("## Performance Highlights", markdown);

            // Verify process names appear in report
            Assert.Contains("goReferenceService", markdown);
            Assert.Contains("rustReferenceService", markdown);

            // Verify explanatory notes exist
            Assert.Contains("*Min (events/s): Lowest throughput recorded during testing. Higher values indicate better worst-case performance.", markdown);
            Assert.Contains("**Std Dev: Standard deviation measures throughput variability. Lower values indicate more consistent performance.", markdown);
            Assert.Contains("***CV%: Coefficient of variation (std dev / mean × 100). Lower values indicate more stable relative performance.", markdown);

            Output.WriteLine($"Comparison report created: {outputPath}");
            Output.WriteLine($"Report size: {new FileInfo(outputPath).Length:N0} bytes");
        }
        finally
        {
            System.FileSystem.CleanupTempFile(outputPath);
        }
    }

    [Fact]
    public async Task GenerateComparisonReport_WithThreeReports_AwardsCorrectMedals()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
        var reports = new[]
        {
            // rustReferenceService: Best runtime (lowest), worst CPU (highest)
            new TestReportBuilder()
                .WithProcessName("rustReferenceService")
                .WithRuntime(90.0)
                .WithCpu(30.0)
                .WithMemory(100.0)
                .WithEventsThroughput(1000.0)
                .Build(),

            // goReferenceService: Medium runtime, best CPU (lowest), worst memory (highest)
            new TestReportBuilder()
                .WithProcessName("goReferenceService")
                .WithRuntime(95.0)
                .WithCpu(20.0)
                .WithMemory(200.0)
                .WithEventsThroughput(1500.0)
                .Build(),

            // dotnet9ReferenceService: Worst runtime (highest), medium CPU, best memory (lowest), best throughput (highest)
            new TestReportBuilder()
                .WithProcessName("dotnet9ReferenceService")
                .WithRuntime(100.0)
                .WithCpu(25.0)
                .WithMemory(80.0)
                .WithEventsThroughput(2000.0)
                .Build()
        };

        try
        {
            // Act
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);

            // Assert
            var markdown = await File.ReadAllTextAsync(outputPath);

            // Runtime (lower is better): rust 🥇, go 🥈, dotnet 🥉
            var runtimeSection = markdown.ExtractSection("## Test Runs Overview");
            Assert.Contains("90.00 🥇", runtimeSection); // rustReferenceService
            Assert.Contains("95.00 🥈", runtimeSection); // goReferenceService
            Assert.Contains("100.00 🥉", runtimeSection); // dotnet9ReferenceService

            // Event throughput (higher is better): dotnet 🥇, go 🥈, rust 🥉
            var eventsThroughputSection = markdown.ExtractSection("### Event Processing Throughput");
            var eventsThroughputLines = eventsThroughputSection.Split('\n');
            var dotnetEventsLine = eventsThroughputLines.First(l => l.Contains("dotnet9ReferenceService"));
            var goEventsLine = eventsThroughputLines.First(l => l.Contains("goReferenceService"));
            var rustEventsLine = eventsThroughputLines.First(l => l.Contains("rustReferenceService"));

            Assert.Contains("2000.00 🥇", dotnetEventsLine);
            Assert.Contains("1500.00 🥈", goEventsLine);
            Assert.Contains("1000.00 🥉", rustEventsLine);

            // CPU usage (lower is better): go 🥇, dotnet 🥈, rust 🥉
            var cpuSection = markdown.ExtractSection("### Process CPU Usage");
            var cpuLines = cpuSection.Split('\n');
            var goCpuLine = cpuLines.First(l => l.Contains("goReferenceService"));
            var dotnetCpuLine = cpuLines.First(l => l.Contains("dotnet9ReferenceService"));
            var rustCpuLine = cpuLines.First(l => l.Contains("rustReferenceService"));

            Assert.Contains("20.00 🥇", goCpuLine);
            Assert.Contains("25.00 🥈", dotnetCpuLine);
            Assert.Contains("30.00 🥉", rustCpuLine);

            // Memory usage (lower is better): dotnet 🥇, rust 🥈, go 🥉
            var memorySection = markdown.ExtractSection("### Process Memory Usage");
            var memoryLines = memorySection.Split('\n');
            var dotnetMemoryLine = memoryLines.First(l => l.Contains("dotnet9ReferenceService"));
            var rustMemoryLine = memoryLines.First(l => l.Contains("rustReferenceService"));
            var goMemoryLine = memoryLines.First(l => l.Contains("goReferenceService"));

            Assert.Contains("80.00 🥇", dotnetMemoryLine);
            Assert.Contains("100.00 🥈", rustMemoryLine);
            Assert.Contains("200.00 🥉", goMemoryLine);

            Output.WriteLine("All medals correctly awarded:");
            Output.WriteLine("  Runtime: rust 🥇, go 🥈, dotnet 🥉");
            Output.WriteLine("  Events Throughput: dotnet 🥇, go 🥈, rust 🥉");
            Output.WriteLine("  CPU: go 🥇, dotnet 🥈, rust 🥉");
            Output.WriteLine("  Memory: dotnet 🥇, rust 🥈, go 🥉");
        }
        finally
        {
            System.FileSystem.CleanupTempFile(outputPath);
        }
    }

    [Fact]
    public async Task GenerateComparisonReport_WithIdenticalValues_AwardsOnlyOneMedal()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");

        // All three services have identical runtime (should all get 🥇, no 🥈 or 🥉)
        var reports = new[]
        {
            new TestReportBuilder()
                .WithProcessName("service1")
                .WithRuntime(100.0)
                .WithCpu(25.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("service2")
                .WithRuntime(100.0)
                .WithCpu(30.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("service3")
                .WithRuntime(100.0)
                .WithCpu(35.0)
                .Build()
        };

        try
        {
            // Act
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);

            // Assert
            var markdown = await File.ReadAllTextAsync(outputPath);
            var runtimeSection = markdown.ExtractSection("## Test Runs Overview");

            // Count medals in runtime column (all should be 🥇 since values are identical)
            var goldMedalCount = runtimeSection.CountOccurrences("100.00 🥇");
            var silverMedalCount = runtimeSection.CountOccurrences("100.00 🥈");
            var bronzeMedalCount = runtimeSection.CountOccurrences("100.00 🥉");

            Assert.Equal(3, goldMedalCount); // All three get gold
            Assert.Equal(0, silverMedalCount); // No silver
            Assert.Equal(0, bronzeMedalCount); // No bronze

            // Verify CPU still gets distinct medals (different values)
            var cpuSection = markdown.ExtractSection("### Process CPU Usage");
            Assert.Contains("25.00 🥇", cpuSection); // service1 (lowest)
            Assert.Contains("30.00 🥈", cpuSection); // service2 (middle)
            Assert.Contains("35.00 🥉", cpuSection); // service3 (highest)

            Output.WriteLine("Medal assignment with identical values verified:");
            Output.WriteLine("  Runtime (all 100.00): All get 🥇");
            Output.WriteLine("  CPU (25, 30, 35): Distinct medals 🥇🥈🥉");
        }
        finally
        {
            System.FileSystem.CleanupTempFile(outputPath);
        }
    }

    [Fact]
    public async Task GenerateComparisonReport_SortsByHigherIsBetter_ThroughputDescending()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
        var reports = new[]
        {
            new TestReportBuilder()
                .WithProcessName("slowService")
                .WithEventsThroughput(500.0)
                .WithApiThroughput(50.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("fastService")
                .WithEventsThroughput(2000.0)
                .WithApiThroughput(200.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("mediumService")
                .WithEventsThroughput(1000.0)
                .WithApiThroughput(100.0)
                .Build()
        };

        try
        {
            // Act
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);

            // Assert
            var markdown = await File.ReadAllTextAsync(outputPath);

            // Event throughput section should be sorted descending (highest first)
            var eventsThroughputSection = markdown.ExtractSection("### Event Processing Throughput");
            var eventsThroughputLines = eventsThroughputSection.Split('\n')
                .Where(l => l.StartsWith("| ") && l.Contains("Service"))
                .ToList();

            Assert.Equal(3, eventsThroughputLines.Count);
            Assert.Contains("fastService", eventsThroughputLines[0]); // Rank 1
            Assert.Contains("mediumService", eventsThroughputLines[1]); // Rank 2
            Assert.Contains("slowService", eventsThroughputLines[2]); // Rank 3

            // API throughput section should also be sorted descending
            var apiThroughputSection = markdown.ExtractSection("### API Throughput");
            var apiThroughputLines = apiThroughputSection.Split('\n')
                .Where(l => l.StartsWith("| ") && l.Contains("Service"))
                .ToList();

            Assert.Equal(3, apiThroughputLines.Count);
            Assert.Contains("fastService", apiThroughputLines[0]); // Rank 1
            Assert.Contains("mediumService", apiThroughputLines[1]); // Rank 2
            Assert.Contains("slowService", apiThroughputLines[2]); // Rank 3

            Output.WriteLine("Throughput sorting verified (higher is better, descending order):");
            Output.WriteLine("  Events: fastService (2000) > mediumService (1000) > slowService (500)");
            Output.WriteLine("  API: fastService (200) > mediumService (100) > slowService (50)");
        }
        finally
        {
            System.FileSystem.CleanupTempFile(outputPath);
        }
    }

    [Fact]
    public async Task GenerateComparisonReport_SortsByLowerIsBetter_MemoryAscending()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
        var reports = new[]
        {
            new TestReportBuilder()
                .WithProcessName("highMemoryService")
                .WithMemory(500.0)
                .WithCpu(50.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("lowMemoryService")
                .WithMemory(100.0)
                .WithCpu(10.0)
                .Build(),

            new TestReportBuilder()
                .WithProcessName("mediumMemoryService")
                .WithMemory(250.0)
                .WithCpu(25.0)
                .Build()
        };

        try
        {
            // Act
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);

            // Assert
            var markdown = await File.ReadAllTextAsync(outputPath);

            // Memory section should be sorted ascending (lowest first)
            var memorySection = markdown.ExtractSection("### Process Memory Usage");
            var memoryLines = memorySection.Split('\n')
                .Where(l => l.StartsWith("| ") && l.Contains("Service"))
                .ToList();

            Assert.Equal(3, memoryLines.Count);
            Assert.Contains("lowMemoryService", memoryLines[0]); // Rank 1
            Assert.Contains("mediumMemoryService", memoryLines[1]); // Rank 2
            Assert.Contains("highMemoryService", memoryLines[2]); // Rank 3

            // CPU section should also be sorted ascending (lowest first)
            var cpuSection = markdown.ExtractSection("### Process CPU Usage");
            var cpuLines = cpuSection.Split('\n')
                .Where(l => l.StartsWith("| ") && l.Contains("Service"))
                .ToList();

            Assert.Equal(3, cpuLines.Count);
            Assert.Contains("lowMemoryService", cpuLines[0]); // Rank 1
            Assert.Contains("mediumMemoryService", cpuLines[1]); // Rank 2
            Assert.Contains("highMemoryService", cpuLines[2]); // Rank 3

            Output.WriteLine("Resource usage sorting verified (lower is better, ascending order):");
            Output.WriteLine("  Memory: lowMemoryService (100) < mediumMemoryService (250) < highMemoryService (500)");
            Output.WriteLine("  CPU: lowMemoryService (10) < mediumMemoryService (25) < highMemoryService (50)");
        }
        finally
        {
            System.FileSystem.CleanupTempFile(outputPath);
        }
    }

    [Fact]
    public async Task GenerateComparisonReport_IncludesPerformanceHighlights()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
        var reports = new[]
        {
            // rustReferenceService: Lowest CPU, highest event CV (least stable events)
            new TestReportBuilder()
                .WithProcessName("rustReferenceService")
                .WithCpu(15.0)
                .WithMemory(150.0)
                .WithEventsThroughput(1000.0, cv: 25.0)
                .WithApiThroughput(100.0, cv: 10.0)
                .Build(),

            // goReferenceService: Fastest events, lowest memory, highest API CV (least stable API)
            new TestReportBuilder()
                .WithProcessName("goReferenceService")
                .WithCpu(20.0)
                .WithMemory(80.0)
                .WithEventsThroughput(2000.0, cv: 5.0)
                .WithApiThroughput(150.0, cv: 20.0)
                .Build(),

            // dotnet9ReferenceService: Fastest API, most stable events (lowest event CV), most stable API (lowest API CV)
            new TestReportBuilder()
                .WithProcessName("dotnet9ReferenceService")
                .WithCpu(25.0)
                .WithMemory(120.0)
                .WithEventsThroughput(1500.0, cv: 3.0)
                .WithApiThroughput(200.0, cv: 2.0)
                .Build()
        };

        try
        {
            // Act
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);

            // Assert
            var markdown = await File.ReadAllTextAsync(outputPath);
            var highlightsSection = markdown.ExtractSection("## Performance Highlights");

            // Verify each highlight category
            Assert.Contains("**Fastest Event Processing:** goReferenceService - 2000.00 events/s", highlightsSection);
            Assert.Contains("**Fastest API Throughput:** dotnet9ReferenceService - 200.00 calls/s", highlightsSection);
            Assert.Contains("**Most Stable Event Processing:** dotnet9ReferenceService - CV: 1.6%", highlightsSection);
            Assert.Contains("**Most Stable API Throughput:** dotnet9ReferenceService - CV: 1.0%", highlightsSection);
            Assert.Contains("**Lowest Average CPU Usage:** rustReferenceService - 15.00%", highlightsSection);
            Assert.Contains("**Lowest Peak Memory Usage:** goReferenceService - 91.00 MB", highlightsSection);

            Output.WriteLine("Performance highlights verified:");
            Output.WriteLine("  Fastest Events: goReferenceService (2000.00 events/s)");
            Output.WriteLine("  Fastest API: dotnet9ReferenceService (200.00 calls/s)");
            Output.WriteLine("  Most Stable Events: dotnet9ReferenceService (CV: 1.6%)");
            Output.WriteLine("  Most Stable API: dotnet9ReferenceService (CV: 1.0%)");
            Output.WriteLine("  Lowest CPU: rustReferenceService (15.00%)");
            Output.WriteLine("  Lowest Memory: goReferenceService (91.00 MB)");
        }
        finally
        {
            System.FileSystem.CleanupTempFile(outputPath);
        }
    }

    [Fact]
    public async Task GenerateComparisonReport_WithEmptyReports_ThrowsArgumentException()
    {
        // Arrange
        var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
        var emptyReports = Array.Empty<TestReport>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, emptyReports);
        });

        Assert.Contains("cannot be empty", exception.Message);

        Output.WriteLine($"Expected exception thrown: {exception.Message}");
    }
}
