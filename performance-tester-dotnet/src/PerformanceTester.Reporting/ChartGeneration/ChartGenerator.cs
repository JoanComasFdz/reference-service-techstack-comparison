using Microsoft.Extensions.Logging;
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.DataLoading;
using PerformanceTester.Reporting.ChartGeneration.ImageComposition;
using PerformanceTester.Reporting.ChartGeneration.PlotBuilders;
using PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;
using PerformanceTester.Reporting.ChartGeneration.Toolbox;
using PerformanceTester.Reporting.ValueObjects;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration;

/// <summary>
/// Generates performance visualization charts using ScottPlot with 5 subplots:
/// 1. Throughput (events/sec + API calls/sec)
/// 2. Service CPU/RAM (dual Y-axes)
/// 3. RabbitMQ CPU/RAM (dual Y-axes)
/// 4. PostgreSQL CPU/RAM (dual Y-axes)
/// 5. System CPU/RAM (dual Y-axes)
/// </summary>
public static class ChartGenerator
{
    /// <summary>
    /// Generates a PNG chart with 5 subplots showing all performance metrics.
    /// </summary>
    /// <param name="outputFolder">Directory to write the chart PNG file.</param>
    /// <param name="testReport">Complete test report data.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full path to the generated chart PNG file.</returns>
    public static async Task<string> GenerateChartAsync(
        ResultsOutputFolder outputFolder,
        TestReport testReport,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var config = ChartConfig.Default;

        ArgumentNullException.ThrowIfNull(testReport, nameof(testReport));

        Directory.CreateDirectory(outputFolder.Value);

        var serviceName = testReport.MonitoredProcess?.Name?.ToLowerInvariant() ?? "unknown";
        var timestamp = testReport.TestDate.ToString("yyyyMMdd_HHmmss");
        var basePath = Path.Combine(outputFolder.Value, $"test-report-{timestamp}-{serviceName}");
        var outputPath = $"{basePath}.chart.png";
        var dataFiles = new DataFilePaths(
            EventsThroughput: $"{basePath}.events-throughput.json",
            ApiThroughput: $"{basePath}.api-throughput.json",
            ResourceMetrics: $"{basePath}.resource-metrics.json",
            RabbitmqMetrics: $"{basePath}.rabbitmq-metrics.json",
            PostgresMetrics: $"{basePath}.postgres-metrics.json",
            SystemMetrics: $"{basePath}.system-metrics.json"
        );

        if (!File.Exists(dataFiles.EventsThroughput) &&
            !File.Exists(dataFiles.ApiThroughput) &&
            !File.Exists(dataFiles.ResourceMetrics))
        {
            throw new InvalidOperationException(
                "No metrics data files found for chart generation. " +
                $"Expected files in directory: {outputFolder.Value}");
        }

        // Load data
        var eventsData = ChartDataLoader.LoadEventsThroughputReport(dataFiles.EventsThroughput, logger);
        var apiData = ChartDataLoader.LoadApiThroughputReport(dataFiles.ApiThroughput, logger);
        var serviceData = ChartDataLoader.LoadProcessResourceReport(dataFiles.ResourceMetrics, logger);
        var rabbitmqData = ChartDataLoader.LoadContainerResourceReport(dataFiles.RabbitmqMetrics, logger);
        var postgresData = ChartDataLoader.LoadContainerResourceReport(dataFiles.PostgresMetrics, logger);
        var systemData = ChartDataLoader.LoadSystemResourceReport(dataFiles.SystemMetrics, logger);

        // Build plots
        var plots = BuildPlots(eventsData, apiData, serviceData, rabbitmqData, postgresData, systemData, config);

        // Add phase overlays
        AddPhaseOverlays(plots, testReport, config);

        // Configure top plot (title, headroom)
        ConfigureTopPlot(plots[0], testReport, config);

        // Configure bottom plot (X-axis label, tick rotation)
        ConfigureBottomPlot(plots[4], config);

        // Synchronize X-axis limits
        SyncXAxisLimits(plots);

        // Render and combine
        var bitmaps = RenderPlots(plots, config);

        try
        {
            ChartImageComposer.CombineAndSave(bitmaps, outputPath);
        }
        finally
        {
            ChartImageComposer.DisposeBitmaps(bitmaps);
        }

        logger.LogInformation("Metrics chart saved to: {OutputPath}", outputPath);

        await Task.CompletedTask;
        return outputPath;
    }

    private static List<Plot> BuildPlots(
        ThroughputReport? eventsData,
        ThroughputReport? apiData,
        ResourceMetricsReport? serviceData,
        ResourceMetricsReport? rabbitmqData,
        ResourceMetricsReport? postgresData,
        ResourceMetricsReport? systemData,
        ChartConfig config)
    {
        return
        [
            ThroughputPlotBuilder.Build(eventsData, apiData, config),
            ServiceMetricsPlotBuilder.Build(serviceData, config),
            RabbitMqMetricsPlotBuilder.Build(rabbitmqData, config),
            PostgresMetricsPlotBuilder.Build(postgresData, config),
            SystemMetricsPlotBuilder.Build(systemData, config)
        ];
    }

    private static void AddPhaseOverlays(List<Plot> plots, TestReport testReport, ChartConfig config)
    {
        foreach (var plot in plots)
        {
            PhaseOverlayRenderer.AddPhaseBoundaries(plot, testReport, config);
        }
    }

    private static void ConfigureTopPlot(Plot plot, TestReport testReport, ChartConfig config)
    {
        // Force auto-scaling before getting limits
        plot.Axes.AutoScale();

        // Add 10% headroom to throughput Y-axis
        var limits = plot.Axes.GetLimits();
        var yMax = limits.Top * 1.1;
        plot.Axes.SetLimitsY(limits.Bottom, yMax);

        // Add phase labels and title
        PhaseOverlayRenderer.AddPhaseLabels(plot, testReport, yMax, config);
        PhaseOverlayRenderer.AddTitle(plot, testReport, config);
    }

    private static void ConfigureBottomPlot(Plot plot, ChartConfig config)
    {
        PlotToolbox.ConfigureBottomAxisLabel(plot, config.Font);

        // Rotate tick labels 45° for readability on bottom axis
        plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleLeft;
    }

    private static void SyncXAxisLimits(List<Plot> plots)
    {
        if (plots.Count == 0)
        {
            return;
        }

        // Force auto-scaling on all plots
        foreach (var plot in plots)
        {
            plot.Axes.AutoScale();
        }

        // Find global X-axis range
        double minX = double.MaxValue;
        double maxX = double.MinValue;

        foreach (var plot in plots)
        {
            var limits = plot.Axes.GetLimits();
            minX = Math.Min(minX, limits.Left);
            maxX = Math.Max(maxX, limits.Right);
        }

        // Apply global range
        foreach (var plot in plots)
        {
            plot.Axes.SetLimitsX(minX, maxX);
        }
    }

    private static List<SkiaSharp.SKBitmap> RenderPlots(List<Plot> plots, ChartConfig config)
    {
        return plots
            .Select((p, i) => ChartImageComposer.RenderPlotToBitmap(
                p,
                i == 0 ? config.Dimensions.ThroughputHeight : config.Dimensions.Height,
                config.Dimensions))
            .ToList();
    }

    private sealed record DataFilePaths(
        string EventsThroughput,
        string ApiThroughput,
        string ResourceMetrics,
        string RabbitmqMetrics,
        string PostgresMetrics,
        string SystemMetrics);
}
