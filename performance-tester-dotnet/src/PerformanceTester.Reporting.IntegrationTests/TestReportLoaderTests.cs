using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using PerformanceTester.Reporting.ReportGeneration;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests;

public sealed class TestReportLoaderTests : IntegrationTest
{
    public TestReportLoaderTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task LoadFromFolderAsync_ShouldRoundTripReportData()
    {
        // Arrange - Generate reports using production ReportGenerator
        var outputDirectory = System.FileSystem.CreateTempDirectory("loader-test");
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var original = new TestReportBuilder()
            .WithProcessName("goReferenceService")
            .WithTestDate(testDate)
            .WithSystemInfo(SystemInfoBuilder.CreateIntelI9())
            .WithEventsThroughputSamples(ThroughputMetricSampleBuilder.CreateWithKnownStatistics(testDate))
            .Build();

        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, original);

            // Act
            var loaded = await TestReportLoader.LoadFromFolderAsync(outputDirectory);

            // Assert
            Assert.Single(loaded);
            var report = loaded[0];

            // Main report fields survived
            Assert.Equal(original.TestDate, report.TestDate);
            Assert.Equal(original.MonitoredProcess.Name, report.MonitoredProcess.Name);
            Assert.Equal(original.Results.Phase1Publish.ThroughputEventsPerSec,
                report.Results.Phase1Publish.ThroughputEventsPerSec);

            // Supplementary data was loaded
            Assert.NotEmpty(report.EventsThroughputSamples);
            Assert.NotEmpty(report.ApiThroughputSamples);
            Assert.NotEmpty(report.ProcessResourceSamples);
            Assert.NotEmpty(report.SystemResourceSamples);
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task LoadFromFolderAsync_WithEmptyFolder_ShouldReturnEmptyList()
    {
        // Arrange
        var outputDirectory = System.FileSystem.CreateTempDirectory("loader-empty");

        try
        {
            // Act
            var loaded = await TestReportLoader.LoadFromFolderAsync(outputDirectory);

            // Assert
            Assert.Empty(loaded);
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task LoadFromFolderAsync_WithMultipleReports_ShouldLoadAll()
    {
        // Arrange
        var outputDirectory = System.FileSystem.CreateTempDirectory("loader-multi");

        var report1 = new TestReportBuilder()
            .WithProcessName("goReferenceService")
            .WithTestDate(new DateTime(2025, 11, 13, 14, 0, 0, DateTimeKind.Utc))
            .WithSystemInfo(SystemInfoBuilder.CreateIntelI9())
            .Build();

        var report2 = new TestReportBuilder()
            .WithProcessName("rustReferenceService")
            .WithTestDate(new DateTime(2025, 11, 13, 15, 0, 0, DateTimeKind.Utc))
            .WithSystemInfo(SystemInfoBuilder.CreateIntelI9())
            .Build();

        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, report1);
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, report2);

            // Act
            var loaded = await TestReportLoader.LoadFromFolderAsync(outputDirectory);

            // Assert
            Assert.Equal(2, loaded.Count);
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task LoadFromFolderAsync_ThroughputMapping_ShouldPreserveRateAndCount()
    {
        // Arrange - Use known samples to verify mapping
        var outputDirectory = System.FileSystem.CreateTempDirectory("loader-mapping");
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
        var knownSamples = ThroughputMetricSampleBuilder.CreateWithKnownStatistics(testDate);

        var original = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithSystemInfo(SystemInfoBuilder.CreateIntelI9())
            .WithEventsThroughputSamples(knownSamples)
            .Build();

        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, original);

            // Act
            var loaded = await TestReportLoader.LoadFromFolderAsync(outputDirectory);

            // Assert - Rate mapping (EventsPerSecond -> Rate)
            var report = loaded[0];
            Assert.Equal(knownSamples.Count, report.EventsThroughputSamples.Count);

            // Values are rounded during serialization (Rate to 2 decimals, ElapsedSeconds to 3)
            // so compare with tolerance
            for (int i = 0; i < knownSamples.Count; i++)
            {
                Assert.Equal(Math.Round(knownSamples[i].Rate, 2),
                    report.EventsThroughputSamples[i].Rate, precision: 2);
                Assert.Equal(knownSamples[i].CumulativeCount,
                    report.EventsThroughputSamples[i].CumulativeCount);
            }
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
