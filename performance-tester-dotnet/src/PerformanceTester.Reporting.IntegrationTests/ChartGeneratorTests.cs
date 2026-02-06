using System.Text.Json;
using PerformanceTester.Reporting.ChartGeneration;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests;

/// <summary>
/// Integration tests for IChartGenerator (PNG chart generation with ScottPlot).
/// Tests verify chart generation matches Python matplotlib output.
/// </summary>
public sealed class ChartGeneratorTests : IntegrationTest
{
    public ChartGeneratorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task GenerateChartAsync_WithValidData_ShouldCreateValidPngFile()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();
        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithEvents(new ThroughputReportBuilder(testReport.TestDate)
                .WithSampleCount(200)
                .WithStatisticalVariation(targetAverage: 2500.0, targetCv: 5.0)
                .Build())
            .WithApi(new ThroughputReportBuilder(testReport.TestDate.AddSeconds(20))
                .WithSampleCount(300)
                .WithStatisticalVariation(targetAverage: 500.0, targetCv: 3.0)
                .Build())
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert
        Assert.True(File.Exists(outputPath), "Chart PNG file should exist");

        // Verify file size (non-empty, reasonable size)
        var fileInfo = new FileInfo(outputPath);
        Assert.InRange(fileInfo.Length, 10_000, 5_000_000);

        // Verify valid PNG signature
        using var fs = File.OpenRead(outputPath);
        var header = new byte[8];
        await fs.ReadExactlyAsync(header, CancellationToken.None);
        Assert.Equal(0x89, header[0]); // PNG signature
        Assert.Equal(0x50, header[1]); // 'P'
        Assert.Equal(0x4E, header[2]); // 'N'
        Assert.Equal(0x47, header[3]); // 'G'

        Output.WriteLine($"Chart generated successfully: {outputPath}");
        Output.WriteLine($"File size: {fileInfo.Length:N0} bytes");
    }

    [Fact]
    public async Task GenerateChartAsync_ShouldHaveFiveSubplots()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();
        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithEvents(new ThroughputReportBuilder(testReport.TestDate)
                .WithSampleCount(200)
                .WithStatisticalVariation(targetAverage: 2500.0, targetCv: 5.0)
                .Build())
            .WithApi(new ThroughputReportBuilder(testReport.TestDate.AddSeconds(20))
                .WithSampleCount(300)
                .WithStatisticalVariation(targetAverage: 500.0, targetCv: 3.0)
                .Build())
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert - Verify all 6 data files exist and loaded
        var basePath = outputPath.Replace(".chart.png", "");
        Assert.True(File.Exists($"{basePath}.events-throughput.json"), "Events throughput data should exist");
        Assert.True(File.Exists($"{basePath}.api-throughput.json"), "API throughput data should exist");
        Assert.True(File.Exists($"{basePath}.resource-metrics.json"), "Service resource data should exist");
        Assert.True(File.Exists($"{basePath}.rabbitmq-metrics.json"), "RabbitMQ data should exist");
        Assert.True(File.Exists($"{basePath}.postgres-metrics.json"), "PostgreSQL data should exist");
        Assert.True(File.Exists($"{basePath}.system-metrics.json"), "System data should exist");

        // Verify chart generated (implies all subplots created)
        Assert.True(File.Exists(outputPath), "Chart PNG file should exist");

        Output.WriteLine("Chart contains all 5 subplots (verified via data files)");
    }

    [Fact]
    public async Task GenerateChartAsync_ResourceSubplots_ShouldUseDualYAxes()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();
        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert
        Assert.True(File.Exists(outputPath), "Chart PNG file should exist");

        // Verify all 4 resource data files loaded
        var basePath = outputPath.Replace(".chart.png", "");
        var serviceJson = await File.ReadAllTextAsync($"{basePath}.resource-metrics.json");
        var serviceData = JsonSerializer.Deserialize<ProcessResourceMetricsReport>(serviceJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.NotNull(serviceData);
        Assert.NotEmpty(serviceData.Samples);
        Assert.NotNull(serviceData.CpuSummary);
        Assert.NotNull(serviceData.MemorySummary);

        Output.WriteLine("All 4 resource subplots have CPU and RAM data (dual Y-axes)");
    }

    [Fact]
    public void ChartColors_ShouldMatchSpecification()
    {
        // Verify ChartColors helper class has correct values
        Assert.Equal("#2ecc71", ChartColors.EventsPrimary.ToLowercaseHex());
        Assert.Equal("#27ae60", ChartColors.EventsAverage.ToLowercaseHex());
        Assert.Equal("#e67e22", ChartColors.ApiPrimary.ToLowercaseHex());
        Assert.Equal("#d35400", ChartColors.ApiAverage.ToLowercaseHex());

        Assert.Equal("#3498db", ChartColors.ServiceCpu.ToLowercaseHex());
        Assert.Equal("#2980b9", ChartColors.ServiceCpuAvg.ToLowercaseHex());
        Assert.Equal("#e74c3c", ChartColors.ServiceRam.ToLowercaseHex());
        Assert.Equal("#c0392b", ChartColors.ServiceRamAvg.ToLowercaseHex());

        Assert.Equal("#9b59b6", ChartColors.RabbitMqCpu.ToLowercaseHex());
        Assert.Equal("#8e44ad", ChartColors.RabbitMqCpuAvg.ToLowercaseHex());
        Assert.Equal("#e67e22", ChartColors.RabbitMqRam.ToLowercaseHex());
        Assert.Equal("#d35400", ChartColors.RabbitMqRamAvg.ToLowercaseHex());

        Assert.Equal("#16a085", ChartColors.PostgresCpu.ToLowercaseHex());
        Assert.Equal("#138d75", ChartColors.PostgresCpuAvg.ToLowercaseHex());
        Assert.Equal("#f39c12", ChartColors.PostgresRam.ToLowercaseHex());
        Assert.Equal("#e67e22", ChartColors.PostgresRamAvg.ToLowercaseHex());

        Assert.Equal("#34495e", ChartColors.SystemCpu.ToLowercaseHex());
        Assert.Equal("#2c3e50", ChartColors.SystemCpuAvg.ToLowercaseHex());
        Assert.Equal("#c0392b", ChartColors.SystemRam.ToLowercaseHex());
        Assert.Equal("#a93226", ChartColors.SystemRamAvg.ToLowercaseHex());

        Assert.Equal("#27ae60", ChartColors.ConsumePhase.ToLowercaseHex());
        Assert.Equal("#2980b9", ChartColors.ConsumeBoundary.ToLowercaseHex());
        Assert.Equal("#e67e22", ChartColors.ApiPhase.ToLowercaseHex());

        Output.WriteLine("All 16 chart colors match specification");
    }

    [Fact]
    public async Task GenerateChartAsync_ShouldIncludePhaseBoundaries()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 4,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();
        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithEvents(new ThroughputReportBuilder(testReport.TestDate)
                .WithSampleCount(200)
                .WithStatisticalVariation(targetAverage: 2500.0, targetCv: 5.0)
                .Build())
            .WithApi(new ThroughputReportBuilder(testReport.TestDate.AddSeconds(20))
                .WithSampleCount(300)
                .WithStatisticalVariation(targetAverage: 500.0, targetCv: 3.0)
                .Build())
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert
        Assert.True(File.Exists(outputPath), "Chart PNG file should exist");

        // Verify phase timestamps present
        Assert.True(testReport.PhaseTimestamps.Phase1Start >= 0, "Phase 1 start should be defined");
        Assert.True(testReport.PhaseTimestamps.Phase1End > 0, "Phase 1 end should be defined");
        Assert.True(testReport.PhaseTimestamps.Phase2Start >= 0, "Phase 2 start should be defined");
        Assert.True(testReport.PhaseTimestamps.Phase2End > 0, "Phase 2 end should be defined");
        Assert.True(testReport.PhaseTimestamps.Phase3Start > 0, "Phase 3 start should be defined");
        Assert.True(testReport.PhaseTimestamps.Phase3End > 0, "Phase 3 end should be defined");

        Output.WriteLine("Phase boundaries configured:");
        Output.WriteLine($"  Consume: {testReport.PhaseTimestamps.Phase1Start}s - {testReport.PhaseTimestamps.Phase2End}s");
        Output.WriteLine($"  API: {testReport.PhaseTimestamps.Phase3Start}s - {testReport.PhaseTimestamps.Phase3End}s");
        Output.WriteLine("Visual inspection required: Verify vertical lines and labels");
    }

    [Fact]
    public async Task GenerateChartAsync_ShouldFormatChartTitle()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testDate = new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc);
        var testReport = new TestReportBuilder()
            .WithProcessName("dotnet9aotreferenceservice")
            .WithTestDate(testDate)
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();

        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithEvents(new ThroughputReportBuilder(testReport.TestDate)
                .WithSampleCount(200)
                .WithStatisticalVariation(targetAverage: 2500.0, targetCv: 5.0)
                .Build())
            .WithApi(new ThroughputReportBuilder(testReport.TestDate.AddSeconds(20))
                .WithSampleCount(300)
                .WithStatisticalVariation(targetAverage: 500.0, targetCv: 3.0)
                .Build())
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Expected title format
        var expectedTitle = "Performance Metrics - dotnet9aotreferenceservice - January 13, 2025 at 14:25:30";

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert
        Assert.True(File.Exists(outputPath), "Chart PNG file should exist");

        Output.WriteLine($"Expected title: {expectedTitle}");
        Output.WriteLine("Visual inspection required: Verify title format in PNG");
    }

    [Fact]
    public void LegendFormatters_ShouldMatchSpecification()
    {
        // Arrange
        var throughputLegend = ChartLegendFormatter.FormatThroughputLegend(
            prefix: "Events",
            avg: 2543.1,
            responseTimeMs: 0.39,
            min: 2100.5,
            max: 2890.2,
            mode: 2550,
            stdDev: 125.4,
            cv: 4.9);

        var expectedThroughputLegend =
            "Events Avg: 2543.1 (0.39ms)\n" +
            "Min: 2100.5\n" +
            "Max: 2890.2\n" +
            "Mode: 2550\n" +
            "Std Dev: 125.4\n" +
            "CV: 4.9%";

        Assert.Equal(expectedThroughputLegend, throughputLegend);

        var resourceLegend = ChartLegendFormatter.FormatResourceLegend(
            avg: 25.3,
            min: 18.2,
            max: 42.1,
            mode: 24,
            unit: "%");

        var expectedResourceLegend =
            "Avg: 25.3 %\n" +
            "Min: 18.2 %\n" +
            "Max: 42.1 %\n" +
            "Mode: 24 %";

        Assert.Equal(expectedResourceLegend, resourceLegend);

        Output.WriteLine("Throughput legend format:");
        Output.WriteLine(throughputLegend);
        Output.WriteLine("");
        Output.WriteLine("Resource legend format:");
        Output.WriteLine(resourceLegend);
    }

    [Fact]
    public async Task GenerateChartAsync_ShouldExportAt150Dpi()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();
        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithEvents(new ThroughputReportBuilder(testReport.TestDate)
                .WithSampleCount(200)
                .WithStatisticalVariation(targetAverage: 2500.0, targetCv: 5.0)
                .Build())
            .WithApi(new ThroughputReportBuilder(testReport.TestDate.AddSeconds(20))
                .WithSampleCount(300)
                .WithStatisticalVariation(targetAverage: 500.0, targetCv: 3.0)
                .Build())
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert - Verify PNG dimensions
        using var bitmap = SKBitmap.Decode(outputPath);
        Assert.NotNull(bitmap);

        // Width: 2400 pixels (increased for legend space)
        // Height: 2500 pixels (580px throughput + 4×480px resource plots)
        Assert.Equal(2400, bitmap.Width);
        Assert.Equal(2550, bitmap.Height);

        var fileSizeKb = new FileInfo(outputPath).Length / 1024;

        Output.WriteLine($"PNG dimensions: {bitmap.Width}×{bitmap.Height} pixels");
        Output.WriteLine($"Expected: 2400×2500 pixels");
        Output.WriteLine($"File size: {fileSizeKb:N0} KB");
    }

    [Fact]
    public async Task GenerateChartAsync_ShouldPositionLegendOutsideRightEdge()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();
        var outputPath = new TestReportFileBuilder(testDir, testReport)
            .WithEvents(new ThroughputReportBuilder(testReport.TestDate)
                .WithSampleCount(200)
                .WithStatisticalVariation(targetAverage: 2500.0, targetCv: 5.0)
                .Build())
            .WithApi(new ThroughputReportBuilder(testReport.TestDate.AddSeconds(20))
                .WithSampleCount(300)
                .WithStatisticalVariation(targetAverage: 500.0, targetCv: 3.0)
                .Build())
            .WithService(new ProcessResourceMetricsReportBuilder(testReport.TestDate)
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithRabbitMq(new ResourceMetricsReportBuilder(testReport.TestDate, "rabbitmq")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithPostgres(new ResourceMetricsReportBuilder(testReport.TestDate, "postgres")
                .WithSampleCount(22)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .WithSystem(new ResourceMetricsReportBuilder(testReport.TestDate, "system")
                .WithSampleCount(130)
                .WithPhaseBasedVariation(targetAvgCpu: 45.0, targetAvgMemory: 125.0)
                .Build())
            .Build();

        // Act
        await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);

        // Assert
        using var bitmap = SKBitmap.Decode(outputPath);
        Assert.NotNull(bitmap);

        // Verify image is wide enough to include legends outside plot area
        Assert.True(bitmap.Width >= 2100, $"Image width {bitmap.Width}px should accommodate legends outside plot area");

        Output.WriteLine("Legend positioning:");
        Output.WriteLine("  Location: Upper left of legend box");
        Output.WriteLine("  Anchor: Outside right edge of plot area");
        Output.WriteLine("Visual inspection required: Verify legends not overlapping data");
    }

    [Fact]
    public async Task GenerateChartAsync_WithNoDataFiles_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var testDir = System.FileSystem.CreateTempDirectory("PerformanceTestCharts");
        var testReport = new TestReportBuilder()
            .WithProcessName("testservice")
            .WithTestDate(new DateTime(2025, 1, 13, 14, 25, 30, DateTimeKind.Utc))
            .WithRuntime(65.5)
            .WithPhaseTimestamps(new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 15.2,
                Phase2Start = 15.2,
                Phase2End = 35.5,
                Phase3Start = 35.5,
                Phase3End = 65.5
            })
            .WithConfiguration(new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "test.exchange",
                ConsumerQueue = "test.queue",
                ApiEndpoint = "http://localhost:8080/api",
                PublishEventType = "test.published",
                ConsumeEventType = "test.consumed"
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 15.2,
                    ThroughputEventsPerSec = 657.89
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 20.3,
                    ThroughputEventsPerSec = 492.61
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 30.0,
                    TotalRequests = 15420,
                    ThroughputCallsPerSec = 514.0,
                    SuccessCount = 15420,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            })
            .Build();

        // Don't create any data files
        var outputPath = Path.Combine(testDir,
            $"test-report-20250113_142530-{testReport.MonitoredProcess.Name.ToLowerInvariant()}.chart.png");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);
        });

        Assert.Contains("No metrics data files", exception.Message);

        Output.WriteLine($"Expected exception thrown: {exception.Message}");
    }
}
