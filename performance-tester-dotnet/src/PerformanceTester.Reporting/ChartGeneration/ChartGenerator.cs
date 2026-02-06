using Microsoft.Extensions.Logging;
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.DataLoading;
using PerformanceTester.Reporting.ChartGeneration.ImageComposition;
using PerformanceTester.Reporting.ChartGeneration.PlotBuilders;
using PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;
using PerformanceTester.Reporting.ChartGeneration.Toolbox;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration;

/// <summary>
/// Default implementation of chart generator.
/// Generates performance visualization charts using ScottPlot with 5 subplots:
/// 1. Throughput (events/sec + API calls/sec)
/// 2. Service CPU/RAM (dual Y-axes)
/// 3. RabbitMQ CPU/RAM (dual Y-axes)
/// 4. PostgreSQL CPU/RAM (dual Y-axes)
/// 5. System CPU/RAM (dual Y-axes)
/// </summary>
internal sealed class ChartGenerator : IChartGenerator
{
    private readonly ILogger<ChartGenerator> _logger;
    private readonly ChartConfig _config;

    public ChartGenerator(ILogger<ChartGenerator> logger)
        : this(logger, ChartConfig.Default)
    {
    }

    public ChartGenerator(ILogger<ChartGenerator> logger, ChartConfig config)
    {
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc />
    public async Task GenerateChartAsync(
        string outputPath,
        TestReport testReport,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath, nameof(outputPath));
        ArgumentNullException.ThrowIfNull(testReport, nameof(testReport));

        EnsureOutputDirectoryExists(outputPath);
        var dataFiles = DeriveDataFilePaths(outputPath);
        ValidateDataFilesExist(dataFiles, outputPath);

        // Load data
        var eventsData = ChartDataLoader.LoadThroughputReport(dataFiles.EventsThroughput, _logger);
        var apiData = ChartDataLoader.LoadThroughputReport(dataFiles.ApiThroughput, _logger);
        var serviceData = ChartDataLoader.LoadProcessResourceReport(dataFiles.ResourceMetrics, _logger);
        var rabbitmqData = ChartDataLoader.LoadResourceReport(dataFiles.RabbitmqMetrics, _logger);
        var postgresData = ChartDataLoader.LoadResourceReport(dataFiles.PostgresMetrics, _logger);
        var systemData = ChartDataLoader.LoadResourceReport(dataFiles.SystemMetrics, _logger);

        // Build plots
        var plots = BuildPlots(eventsData, apiData, serviceData, rabbitmqData, postgresData, systemData);

        // Add phase overlays
        AddPhaseOverlays(plots, testReport);

        // Configure top plot (title, headroom)
        ConfigureTopPlot(plots[0], testReport);

        // Configure bottom plot (X-axis label, tick rotation)
        ConfigureBottomPlot(plots[4]);

        // Synchronize X-axis limits
        SyncXAxisLimits(plots);

        // Render and combine
        var bitmaps = RenderPlots(plots);

        try
        {
            ChartImageComposer.CombineAndSave(bitmaps, outputPath);
        }
        finally
        {
            ChartImageComposer.DisposeBitmaps(bitmaps);
        }

        _logger.LogInformation("Metrics chart saved to: {OutputPath}", outputPath);

        await Task.CompletedTask;
    }

    private static void EnsureOutputDirectoryExists(string outputPath)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static DataFilePaths DeriveDataFilePaths(string outputPath)
    {
        var basePath = outputPath.Replace(".chart.png", "");
        return new DataFilePaths(
            EventsThroughput: $"{basePath}.events-throughput.json",
            ApiThroughput: $"{basePath}.api-throughput.json",
            ResourceMetrics: $"{basePath}.resource-metrics.json",
            RabbitmqMetrics: $"{basePath}.rabbitmq-metrics.json",
            PostgresMetrics: $"{basePath}.postgres-metrics.json",
            SystemMetrics: $"{basePath}.system-metrics.json"
        );
    }

    private static void ValidateDataFilesExist(DataFilePaths dataFiles, string outputPath)
    {
        if (!File.Exists(dataFiles.EventsThroughput) &&
            !File.Exists(dataFiles.ApiThroughput) &&
            !File.Exists(dataFiles.ResourceMetrics))
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            throw new InvalidOperationException(
                "No metrics data files found for chart generation. " +
                $"Expected files in directory: {outputDirectory}");
        }
    }

    private List<Plot> BuildPlots(
        ThroughputReport? eventsData,
        ThroughputReport? apiData,
        ResourceMetricsReport? serviceData,
        ResourceMetricsReport? rabbitmqData,
        ResourceMetricsReport? postgresData,
        ResourceMetricsReport? systemData)
    {
        return
        [
            ThroughputPlotBuilder.Build(eventsData, apiData, _config),
            ServiceMetricsPlotBuilder.Build(serviceData, _config),
            RabbitMqMetricsPlotBuilder.Build(rabbitmqData, _config),
            PostgresMetricsPlotBuilder.Build(postgresData, _config),
            SystemMetricsPlotBuilder.Build(systemData, _config)
        ];
    }

    private void AddPhaseOverlays(List<Plot> plots, TestReport testReport)
    {
        foreach (var plot in plots)
        {
            PhaseOverlayRenderer.AddPhaseBoundaries(plot, testReport, _config);
        }
    }

    private void ConfigureTopPlot(Plot plot, TestReport testReport)
    {
        // Force auto-scaling before getting limits
        plot.Axes.AutoScale();

        // Add 10% headroom to throughput Y-axis
        var limits = plot.Axes.GetLimits();
        var yMax = limits.Top * 1.1;
        plot.Axes.SetLimitsY(limits.Bottom, yMax);

        // Add phase labels and title
        PhaseOverlayRenderer.AddPhaseLabels(plot, testReport, yMax, _config);
        PhaseOverlayRenderer.AddTitle(plot, testReport, _config);
    }

    private void ConfigureBottomPlot(Plot plot)
    {
        PlotToolbox.ConfigureBottomAxisLabel(plot, _config.Font);

        // Rotate tick labels 45° for readability on bottom axis
        plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleLeft;
    }

    private static void SyncXAxisLimits(List<Plot> plots)
    {
        if (plots.Count == 0)
            return;

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

    private List<SkiaSharp.SKBitmap> RenderPlots(List<Plot> plots)
    {
        return plots
            .Select((p, i) => ChartImageComposer.RenderPlotToBitmap(
                p,
                i == 0 ? _config.Dimensions.ThroughputHeight : _config.Dimensions.Height,
                _config.Dimensions))
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
