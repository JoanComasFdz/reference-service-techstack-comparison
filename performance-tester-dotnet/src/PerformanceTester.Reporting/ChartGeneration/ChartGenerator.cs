using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScottPlot;
using SkiaSharp;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // Individual subplot dimensions (width × height)
    private const int PlotWidth = 2100;
    private const int PlotHeight = 480; // 2400 total / 5 subplots = 480 each

    public ChartGenerator(ILogger<ChartGenerator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task GenerateChartAsync(
        string outputPath,
        TestReport testReport,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath, nameof(outputPath));
        ArgumentNullException.ThrowIfNull(testReport, nameof(testReport));

        // Ensure output directory exists
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Derive data file paths from chart output path
        var basePath = outputPath.Replace(".chart.png", "");
        var eventsThroughputFile = $"{basePath}.events-throughput.json";
        var apiThroughputFile = $"{basePath}.api-throughput.json";
        var resourceMetricsFile = $"{basePath}.resource-metrics.json";
        var rabbitmqMetricsFile = $"{basePath}.rabbitmq-metrics.json";
        var postgresMetricsFile = $"{basePath}.postgres-metrics.json";
        var systemMetricsFile = $"{basePath}.system-metrics.json";

        // Verify at least one data file exists
        if (!File.Exists(eventsThroughputFile) &&
            !File.Exists(apiThroughputFile) &&
            !File.Exists(resourceMetricsFile))
        {
            throw new InvalidOperationException(
                "No metrics data files found for chart generation. " +
                $"Expected files in directory: {outputDirectory}");
        }

        // Load data from JSON files
        var eventsData = LoadThroughputReport(eventsThroughputFile);
        var apiData = LoadThroughputReport(apiThroughputFile);
        var serviceData = LoadProcessResourceReport(resourceMetricsFile);
        var rabbitmqData = LoadResourceReport(rabbitmqMetricsFile);
        var postgresData = LoadResourceReport(postgresMetricsFile);
        var systemData = LoadResourceReport(systemMetricsFile);

        // Create individual plots
        var plots = new List<Plot>
        {
            CreateThroughputPlot(eventsData, apiData),
            CreateResourcePlot(serviceData, "Service CPU (%)", "Service RAM (MB)",
                ChartColors.ServiceCpu, ChartColors.ServiceCpuAvg,
                ChartColors.ServiceRam, ChartColors.ServiceRamAvg),
            CreateResourcePlot(rabbitmqData, "RabbitMQ CPU (%)", "RabbitMQ RAM (MB)",
                ChartColors.RabbitMqCpu, ChartColors.RabbitMqCpuAvg,
                ChartColors.RabbitMqRam, ChartColors.RabbitMqRamAvg),
            CreateResourcePlot(postgresData, "PostgreSQL CPU (%)", "PostgreSQL RAM (MB)",
                ChartColors.PostgresCpu, ChartColors.PostgresCpuAvg,
                ChartColors.PostgresRam, ChartColors.PostgresRamAvg),
            CreateResourcePlot(systemData, "System CPU (%)", "System RAM (MB)",
                ChartColors.SystemCpu, ChartColors.SystemCpuAvg,
                ChartColors.SystemRam, ChartColors.SystemRamAvg)
        };

        // Add phase boundaries to all plots
        foreach (var plot in plots)
        {
            AddPhaseBoundaries(plot, testReport);
        }

        // Add phase labels to top plot
        AddPhaseLabels(plots[0], testReport);

        // Add title to top plot
        var humanDate = testReport.TestDate.ToString("MMMM dd, yyyy");
        var humanTime = testReport.TestDate.ToString("HH:mm:ss");
        var title = $"Performance Metrics - {testReport.MonitoredProcess.Name} - {humanDate} at {humanTime}";
        plots[0].Title(title);

        // Only show X-axis label on bottom plot
        plots[4].Axes.Bottom.Label.Text = "Time";
        plots[4].Axes.Bottom.Label.Bold = true;
        plots[4].Axes.Bottom.Label.FontSize = 11;

        // Synchronize X-axis limits across all plots
        SyncXAxisLimits(plots);

        // Render each plot to a bitmap
        var bitmaps = plots.Select(p => RenderPlotToBitmap(p)).ToList();

        // Combine bitmaps vertically
        CombineBitmapsVertically(bitmaps, outputPath);

        // Dispose resources
        foreach (var bitmap in bitmaps)
        {
            bitmap.Dispose();
        }

        _logger.LogInformation("Metrics chart saved to: {OutputPath}", outputPath);

        await Task.CompletedTask;
    }

    private Plot CreateThroughputPlot(
        ThroughputReport? eventsData,
        ThroughputReport? apiData)
    {
        var plot = new Plot();
        plot.Axes.Left.Label.Text = "Throughput (per second)";
        plot.Axes.Left.Label.Bold = true;
        plot.Axes.Left.Label.FontSize = 11;

        // Enable grid (Y-axis only)
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#cccccc").WithAlpha(0.3);

        // Plot events throughput
        if (eventsData != null && eventsData.Samples.Count > 0)
        {
            var timestamps = eventsData.Samples
                .Select(s => DateTime.Parse(s.Timestamp).ToOADate())
                .ToArray();
            var rates = eventsData.Samples
                .Select(s => s.ThroughputRate)
                .ToArray();

            // Primary line with fill
            var eventsScatter = plot.Add.Scatter(timestamps, rates);
            eventsScatter.Color = ChartColors.EventsPrimary;
            eventsScatter.LineWidth = 2f;
            eventsScatter.FillY = true;
            eventsScatter.FillYColor = ChartColors.EventsPrimary.WithAlpha(0.3);
            eventsScatter.LegendText = FormatThroughputLegend(
                "Events",
                eventsData.Summary.AvgRate,
                eventsData.Summary.AvgResponseTimeMs,
                eventsData.Summary.MinRate,
                eventsData.Summary.PeakRate,
                (int)Math.Round(eventsData.Summary.AvgRate),
                eventsData.Summary.StdDevRate,
                eventsData.Summary.CvRate);

            // Average line
            var eventsAvg = plot.Add.HorizontalLine(eventsData.Summary.AvgRate);
            eventsAvg.Color = ChartColors.EventsAverage;
            eventsAvg.LineWidth = 1.5f;
            eventsAvg.LinePattern = LinePattern.Dashed;
        }

        // Plot API throughput
        if (apiData != null && apiData.Samples.Count > 0)
        {
            var timestamps = apiData.Samples
                .Select(s => DateTime.Parse(s.Timestamp).ToOADate())
                .ToArray();
            var rates = apiData.Samples
                .Select(s => s.ThroughputRate)
                .ToArray();

            // Primary line with fill
            var apiScatter = plot.Add.Scatter(timestamps, rates);
            apiScatter.Color = ChartColors.ApiPrimary;
            apiScatter.LineWidth = 2f;
            apiScatter.FillY = true;
            apiScatter.FillYColor = ChartColors.ApiPrimary.WithAlpha(0.3);
            apiScatter.LegendText = FormatThroughputLegend(
                "API",
                apiData.Summary.AvgRate,
                apiData.Summary.AvgResponseTimeMs,
                apiData.Summary.MinRate,
                apiData.Summary.PeakRate,
                (int)Math.Round(apiData.Summary.AvgRate),
                apiData.Summary.StdDevRate,
                apiData.Summary.CvRate);

            // Average line
            var apiAvg = plot.Add.HorizontalLine(apiData.Summary.AvgRate);
            apiAvg.Color = ChartColors.ApiAverage;
            apiAvg.LineWidth = 1.5f;
            apiAvg.LinePattern = LinePattern.Dashed;
        }

        // Configure legend
        plot.ShowLegend(Alignment.UpperLeft);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = 9;
        plot.Layout.Fixed(new PixelPadding(left: 60, right: 300, bottom: 50, top: 50));

        return plot;
    }

    private Plot CreateResourcePlot(
        ResourceMetricsReport? data,
        string cpuLabel,
        string ramLabel,
        Color cpuColor,
        Color cpuAvgColor,
        Color ramColor,
        Color ramAvgColor)
    {
        var plot = new Plot();

        if (data == null || data.Samples.Count == 0)
        {
            // Empty subplot with labels
            plot.Axes.Left.Label.Text = cpuLabel;
            plot.Axes.Right.Label.Text = ramLabel;
            return plot;
        }

        // LEFT AXIS: CPU %
        plot.Axes.Left.Label.Text = cpuLabel;
        plot.Axes.Left.Label.ForeColor = cpuColor;
        plot.Axes.Left.Label.Bold = true;
        plot.Axes.Left.Label.FontSize = 11;

        var timestamps = data.Samples
            .Select(s => DateTime.Parse(s.Timestamp).ToOADate())
            .ToArray();
        var cpuValues = data.Samples
            .Select(s => s.CpuPercent)
            .ToArray();

        // CPU primary line with fill
        var cpuScatter = plot.Add.Scatter(timestamps, cpuValues);
        cpuScatter.Color = cpuColor;
        cpuScatter.LineWidth = 2f;
        cpuScatter.FillY = true;
        cpuScatter.FillYColor = cpuColor.WithAlpha(0.3);
        cpuScatter.LegendText = FormatResourceLegend(
            data.CpuSummary.Avg,
            data.CpuSummary.Min,
            data.CpuSummary.Max,
            data.CpuSummary.Mode,
            data.CpuSummary.Unit);

        // CPU average line
        var cpuAvgLine = plot.Add.HorizontalLine(data.CpuSummary.Avg);
        cpuAvgLine.Color = cpuAvgColor;
        cpuAvgLine.LineWidth = 1.5f;
        cpuAvgLine.LinePattern = LinePattern.Dashed;

        // RIGHT AXIS: RAM MB
        var ramValues = data.Samples
            .Select(s => s.MemoryMb)
            .ToArray();

        // RAM primary line with fill
        var ramScatter = plot.Add.Scatter(timestamps, ramValues);
        ramScatter.Color = ramColor;
        ramScatter.LineWidth = 2f;
        ramScatter.Axes.YAxis = plot.Axes.Right; // Use right axis
        ramScatter.FillY = true;
        ramScatter.FillYColor = ramColor.WithAlpha(0.2); // Less alpha for RAM
        ramScatter.LegendText = FormatResourceLegend(
            data.MemorySummary.Avg,
            data.MemorySummary.Min,
            data.MemorySummary.Max,
            data.MemorySummary.Mode,
            data.MemorySummary.Unit);

        // RAM average line
        var ramAvgLine = plot.Add.HorizontalLine(data.MemorySummary.Avg);
        ramAvgLine.Color = ramAvgColor;
        ramAvgLine.LineWidth = 1.5f;
        ramAvgLine.LinePattern = LinePattern.Dashed;
        ramAvgLine.Axes.YAxis = plot.Axes.Right;

        // Configure right axis
        plot.Axes.Right.Label.Text = ramLabel;
        plot.Axes.Right.Label.ForeColor = ramColor;
        plot.Axes.Right.Label.Bold = true;
        plot.Axes.Right.Label.FontSize = 11;

        // Enable grid (Y-axis only)
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#cccccc").WithAlpha(0.3);

        // Configure legend
        plot.ShowLegend(Alignment.UpperLeft);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = 9;
        plot.Layout.Fixed(new PixelPadding(left: 60, right: 300, bottom: 50, top: 50));

        return plot;
    }

    private void AddPhaseBoundaries(Plot plot, TestReport testReport)
    {
        var phase1Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase1Start);
        var phase2End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase2End);
        var phase3Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3Start);
        var phase3End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3End);

        var boundaries = new[]
        {
            (Time: phase1Start, Color: ChartColors.ConsumePhase),
            (Time: phase2End, Color: ChartColors.ConsumeBoundary),
            (Time: phase3Start, Color: ChartColors.ApiPhase),
            (Time: phase3End, Color: ChartColors.ApiPhase)
        };

        foreach (var (time, color) in boundaries)
        {
            var oaDate = time.ToOADate();
            var vline = plot.Add.VerticalLine(oaDate);
            vline.Color = color.WithAlpha(0.6);
            vline.LinePattern = LinePattern.Dotted;
            vline.LineWidth = 1.5f;
        }
    }

    private void AddPhaseLabels(Plot plot, TestReport testReport)
    {
        var phase1Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase1Start);
        var phase2End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase2End);
        var phase3Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3Start);
        var phase3End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3End);

        // Consume phase label
        var consumeMidTime = phase1Start.AddTicks((phase2End - phase1Start).Ticks / 2);
        var consumeLabel = testReport.Configuration.NumEvents > 0
            ? $"Consume: {testReport.Configuration.NumEvents}"
            : "Consume";

        var consumeText = plot.Add.Text(consumeLabel, consumeMidTime.ToOADate(), 0);
        consumeText.LabelFontColor = ChartColors.ConsumePhase;
        consumeText.LabelFontSize = 9;
        consumeText.LabelBold = true;
        consumeText.LabelAlignment = Alignment.UpperCenter;
        consumeText.OffsetY = -40; // Position near top

        // API phase label
        var apiMidTime = phase3Start.AddTicks((phase3End - phase3Start).Ticks / 2);
        var apiLabel = "API";
        if (!string.IsNullOrEmpty(testReport.Configuration.ApiDuration))
        {
            apiLabel = testReport.Configuration.ApiConcurrentWorkers > 0
                ? $"API: {testReport.Configuration.ApiDuration} ({testReport.Configuration.ApiConcurrentWorkers}w)"
                : $"API: {testReport.Configuration.ApiDuration}";
        }

        var apiText = plot.Add.Text(apiLabel, apiMidTime.ToOADate(), 0);
        apiText.LabelFontColor = ChartColors.ApiPhase;
        apiText.LabelFontSize = 9;
        apiText.LabelBold = true;
        apiText.LabelAlignment = Alignment.UpperCenter;
        apiText.OffsetY = -40; // Position near top
    }

    private void SyncXAxisLimits(List<Plot> plots)
    {
        if (plots.Count == 0)
            return;

        // Find the global X-axis range across all plots
        double minX = double.MaxValue;
        double maxX = double.MinValue;

        foreach (var plot in plots)
        {
            var limits = plot.Axes.GetLimits();
            minX = Math.Min(minX, limits.Left);
            maxX = Math.Max(maxX, limits.Right);
        }

        // Apply the global range to all plots
        foreach (var plot in plots)
        {
            plot.Axes.SetLimitsX(minX, maxX);
        }
    }

    private SKBitmap RenderPlotToBitmap(Plot plot)
    {
        var image = plot.GetImage(PlotWidth, PlotHeight);
        return SKBitmap.Decode(image.GetImageBytes());
    }

    private void CombineBitmapsVertically(List<SKBitmap> bitmaps, string outputPath)
    {
        if (bitmaps.Count == 0)
            throw new ArgumentException("No bitmaps to combine", nameof(bitmaps));

        var totalHeight = bitmaps.Sum(b => b.Height);
        var width = bitmaps[0].Width;

        using var combinedBitmap = new SKBitmap(width, totalHeight);
        using var canvas = new SKCanvas(combinedBitmap);

        canvas.Clear(SKColors.White);

        int yOffset = 0;
        foreach (var bitmap in bitmaps)
        {
            canvas.DrawBitmap(bitmap, 0, yOffset);
            yOffset += bitmap.Height;
        }

        using var fileStream = File.OpenWrite(outputPath);
        combinedBitmap.Encode(fileStream, SKEncodedImageFormat.Png, 100);
    }

    private ThroughputReport? LoadThroughputReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ThroughputReport>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load throughput report from {FilePath}", filePath);
            return null;
        }
    }

    private ResourceMetricsReport? LoadResourceReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ResourceMetricsReport>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load resource report from {FilePath}", filePath);
            return null;
        }
    }

    private ResourceMetricsReport? LoadProcessResourceReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var processReport = JsonSerializer.Deserialize<ProcessResourceMetricsReport>(json, JsonOptions);

            if (processReport == null)
                return null;

            // Convert ProcessResourceMetricsReport to ResourceMetricsReport for charting
            // (charts don't need thread info, just CPU and memory)
            return new ResourceMetricsReport
            {
                TestDate = processReport.TestDate,
                SamplingIntervalMs = processReport.SamplingIntervalMs,
                Samples = processReport.Samples.Select(s => new ResourceSampleJson
                {
                    Timestamp = s.Timestamp,
                    ElapsedSeconds = s.ElapsedSeconds,
                    CpuPercent = s.CpuPercent,
                    MemoryMb = s.MemoryRssMb // Map RSS memory to generic memory
                }).ToList(),
                CpuSummary = processReport.CpuSummary,
                MemorySummary = processReport.MemorySummary
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load process resource report from {FilePath}", filePath);
            return null;
        }
    }

    private string FormatThroughputLegend(
        string prefix,
        double avg,
        double responseTimeMs,
        double min,
        double max,
        int mode,
        double stdDev,
        double cv)
    {
        return $"{prefix} Avg: {avg:F1} ({responseTimeMs:F2}ms)\n" +
               $"Min: {min:F1}\n" +
               $"Max: {max:F1}\n" +
               $"Mode: {mode}\n" +
               $"Std Dev: {stdDev:F1}\n" +
               $"CV: {cv:F1}%";
    }

    private string FormatResourceLegend(
        double avg,
        double min,
        double max,
        int mode,
        string unit)
    {
        return $"Avg: {avg:F1} {unit}\n" +
               $"Min: {min:F1} {unit}\n" +
               $"Max: {max:F1} {unit}\n" +
               $"Mode: {mode} {unit}";
    }
}
