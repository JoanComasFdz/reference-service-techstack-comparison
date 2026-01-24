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

    /// <summary>
    /// Configures the bottom axis to display OADate values as HH:mm:ss time format.
    /// </summary>
    private static void ConfigureTimeAxis(Plot plot)
    {
        // Use DateTimeAutomatic tick generator with custom label formatter
        var tickGen = new ScottPlot.TickGenerators.DateTimeAutomatic
        {
            LabelFormatter = dt => dt.ToString("HH:mm:ss")
        };
        plot.Axes.Bottom.TickGenerator = tickGen;
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
            CreateResourcePlot(systemData, "Overall System CPU (%)", "Overall System RAM (MB)",
                ChartColors.SystemCpu, ChartColors.SystemCpuAvg,
                ChartColors.SystemRam, ChartColors.SystemRamAvg)
        };

        // Add phase boundaries to all plots
        foreach (var plot in plots)
        {
            AddPhaseBoundaries(plot, testReport);
        }

        // Force auto-scaling before getting limits (auto-scale normally happens at render time)
        plots[0].Axes.AutoScale();

        // Add 10% headroom to throughput Y-axis and get limits for phase labels
        var throughputLimits = plots[0].Axes.GetLimits();
        var throughputYMax = throughputLimits.Top * 1.1;
        plots[0].Axes.SetLimitsY(throughputLimits.Bottom, throughputYMax);

        // Add phase labels to top plot (positioned at 95% of Y-max)
        AddPhaseLabels(plots[0], testReport, throughputYMax);

        // Add title to top plot
        var humanDate = testReport.TestDate.ToString("MMMM dd, yyyy");
        var humanTime = testReport.TestDate.ToString("HH:mm:ss");
        var title = $"Performance Metrics - {testReport.MonitoredProcess.Name} - {humanDate} at {humanTime}";
        plots[0].Title(title);

        // Only show X-axis label on bottom plot
        plots[4].Axes.Bottom.Label.Text = "Time";
        plots[4].Axes.Bottom.Label.Bold = true;
        plots[4].Axes.Bottom.Label.FontSize = 11;

        // Configure tick label rotation (45 degrees) on the bottom plot only
        plots[4].Axes.Bottom.TickLabelStyle.Rotation = 45;
        plots[4].Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleLeft;

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

            // Primary line with fill - just the series name
            var eventsScatter = plot.Add.Scatter(timestamps, rates);
            eventsScatter.Color = ChartColors.EventsPrimary;
            eventsScatter.LineWidth = 2f;
            eventsScatter.FillY = true;
            eventsScatter.FillYColor = ChartColors.EventsPrimary.WithAlpha(0.3);
            eventsScatter.LegendText = "Consumed Events/sec";

            // Average line - with stats
            var eventsAvg = plot.Add.HorizontalLine(eventsData.Summary.AvgRate);
            eventsAvg.Color = ChartColors.EventsAverage;
            eventsAvg.LineWidth = 1.5f;
            eventsAvg.LinePattern = LinePattern.Dashed;
            eventsAvg.LegendText = FormatThroughputLegend(
                "Events",
                eventsData.Summary.AvgRate,
                eventsData.Summary.AvgResponseTimeMs,
                eventsData.Summary.MinRate,
                eventsData.Summary.PeakRate,
                (int)Math.Round(eventsData.Summary.AvgRate),
                eventsData.Summary.StdDevRate,
                eventsData.Summary.CvRate);
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

            // Primary line with fill - just the series name
            var apiScatter = plot.Add.Scatter(timestamps, rates);
            apiScatter.Color = ChartColors.ApiPrimary;
            apiScatter.LineWidth = 2f;
            apiScatter.FillY = true;
            apiScatter.FillYColor = ChartColors.ApiPrimary.WithAlpha(0.3);
            apiScatter.LegendText = "API calls/sec";

            // Average line - with stats
            var apiAvg = plot.Add.HorizontalLine(apiData.Summary.AvgRate);
            apiAvg.Color = ChartColors.ApiAverage;
            apiAvg.LineWidth = 1.5f;
            apiAvg.LinePattern = LinePattern.Dashed;
            apiAvg.LegendText = FormatThroughputLegend(
                "API",
                apiData.Summary.AvgRate,
                apiData.Summary.AvgResponseTimeMs,
                apiData.Summary.MinRate,
                apiData.Summary.PeakRate,
                (int)Math.Round(apiData.Summary.AvgRate),
                apiData.Summary.StdDevRate,
                apiData.Summary.CvRate);
        }

        // Configure legend outside the plot area on the right
        plot.ShowLegend(Edge.Right);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = 11;
        plot.Layout.Fixed(new PixelPadding(left: 60, right: 220, bottom: 50, top: 50));

        // Apply custom time tick generator for X-axis (HH:mm:ss format)
        ConfigureTimeAxis(plot);

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

            // Still configure time axis and layout for empty plots
            plot.Layout.Fixed(new PixelPadding(left: 60, right: 220, bottom: 50, top: 50));
            ConfigureTimeAxis(plot);
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

        // CPU primary line with fill - simple label like Python
        var cpuScatter = plot.Add.Scatter(timestamps, cpuValues);
        cpuScatter.Color = cpuColor;
        cpuScatter.LineWidth = 2f;
        cpuScatter.FillY = true;
        cpuScatter.FillYColor = cpuColor.WithAlpha(0.3);
        cpuScatter.LegendText = "CPU %";

        // CPU average line - stats in legend like Python
        var cpuAvgLine = plot.Add.HorizontalLine(data.CpuSummary.Avg);
        cpuAvgLine.Color = cpuAvgColor;
        cpuAvgLine.LineWidth = 1.5f;
        cpuAvgLine.LinePattern = LinePattern.Dashed;
        cpuAvgLine.LegendText = FormatResourceLegend(
            data.CpuSummary.Avg,
            data.CpuSummary.Min,
            data.CpuSummary.Max,
            data.CpuSummary.Mode,
            data.CpuSummary.Unit);

        // RIGHT AXIS: RAM MB
        var ramValues = data.Samples
            .Select(s => s.MemoryMb)
            .ToArray();

        // RAM primary line with fill - simple label like Python
        var ramScatter = plot.Add.Scatter(timestamps, ramValues);
        ramScatter.Color = ramColor;
        ramScatter.LineWidth = 2f;
        ramScatter.Axes.YAxis = plot.Axes.Right; // Use right axis
        ramScatter.FillY = true;
        ramScatter.FillYColor = ramColor.WithAlpha(0.2); // Less alpha for RAM
        ramScatter.LegendText = "RAM";

        // RAM average line - stats in legend like Python
        var ramAvgLine = plot.Add.HorizontalLine(data.MemorySummary.Avg);
        ramAvgLine.Color = ramAvgColor;
        ramAvgLine.LineWidth = 1.5f;
        ramAvgLine.LinePattern = LinePattern.Dashed;
        ramAvgLine.Axes.YAxis = plot.Axes.Right;
        ramAvgLine.LegendText = FormatResourceLegend(
            data.MemorySummary.Avg,
            data.MemorySummary.Min,
            data.MemorySummary.Max,
            data.MemorySummary.Mode,
            data.MemorySummary.Unit);

        // Configure right axis
        plot.Axes.Right.Label.Text = ramLabel;
        plot.Axes.Right.Label.ForeColor = ramColor;
        plot.Axes.Right.Label.Bold = true;
        plot.Axes.Right.Label.FontSize = 11;

        // Enable grid (Y-axis only)
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#cccccc").WithAlpha(0.3);

        // Configure legend outside the plot area on the right
        plot.ShowLegend(Edge.Right);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = 11;
        plot.Layout.Fixed(new PixelPadding(left: 60, right: 220, bottom: 50, top: 50));

        // Apply custom time tick generator for X-axis (HH:mm:ss format)
        ConfigureTimeAxis(plot);

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

    private void AddPhaseLabels(Plot plot, TestReport testReport, double yMax)
    {
        var phase1Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase1Start);
        var phase2End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase2End);
        var phase3Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3Start);
        var phase3End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3End);

        // Position at 95% of Y-axis max (like Python)
        var labelYPosition = yMax * 0.95;

        // Consume phase label
        var consumeMidTime = phase1Start.AddTicks((phase2End - phase1Start).Ticks / 2);
        var consumeLabel = testReport.Configuration.NumEvents > 0
            ? $"Consume: {testReport.Configuration.NumEvents}"
            : "Consume";

        var consumeText = plot.Add.Text(consumeLabel, consumeMidTime.ToOADate(), labelYPosition);
        consumeText.LabelFontColor = ChartColors.ConsumePhase;
        consumeText.LabelFontSize = 9;
        consumeText.LabelBold = true;
        consumeText.LabelAlignment = Alignment.UpperCenter;

        // API phase label
        var apiMidTime = phase3Start.AddTicks((phase3End - phase3Start).Ticks / 2);
        var apiLabel = "API";
        if (!string.IsNullOrEmpty(testReport.Configuration.ApiDuration))
        {
            apiLabel = testReport.Configuration.ApiConcurrentWorkers > 0
                ? $"API: {testReport.Configuration.ApiDuration} ({testReport.Configuration.ApiConcurrentWorkers}w)"
                : $"API: {testReport.Configuration.ApiDuration}";
        }

        var apiText = plot.Add.Text(apiLabel, apiMidTime.ToOADate(), labelYPosition);
        apiText.LabelFontColor = ChartColors.ApiPhase;
        apiText.LabelFontSize = 9;
        apiText.LabelBold = true;
        apiText.LabelAlignment = Alignment.UpperCenter;
    }

    private void SyncXAxisLimits(List<Plot> plots)
    {
        if (plots.Count == 0)
            return;

        // Force auto-scaling on all plots first (auto-scale normally happens at render time)
        foreach (var plot in plots)
        {
            plot.Axes.AutoScale();
        }

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
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse samples - handle both events and API formats
            var samples = new List<ThroughputSampleJson>();
            if (root.TryGetProperty("samples", out var samplesArray))
            {
                foreach (var sample in samplesArray.EnumerateArray())
                {
                    var timestamp = sample.GetProperty("timestamp").GetString() ?? "";
                    var elapsedSeconds = sample.GetProperty("elapsed_seconds").GetDouble();
                    
                    // Get rate from either events_per_second or calls_per_second
                    double rate = 0;
                    if (sample.TryGetProperty("events_per_second", out var eventsRate))
                        rate = eventsRate.GetDouble();
                    else if (sample.TryGetProperty("calls_per_second", out var callsRate))
                        rate = callsRate.GetDouble();
                    
                    // Get count from either total_events or total_calls
                    int count = 0;
                    if (sample.TryGetProperty("total_events", out var totalEvents))
                        count = totalEvents.GetInt32();
                    else if (sample.TryGetProperty("total_calls", out var totalCalls))
                        count = totalCalls.GetInt32();

                    samples.Add(new ThroughputSampleJson
                    {
                        Timestamp = timestamp,
                        ElapsedSeconds = elapsedSeconds,
                        EventsPerSecond = rate,
                        TotalEvents = count
                    });
                }
            }

            // Parse summary - handle both events and API formats
            var summary = root.GetProperty("summary");
            
            double avgRate = GetDoubleFromEither(summary, "avg_events_per_second", "avg_calls_per_second");
            double peakRate = GetDoubleFromEither(summary, "peak_events_per_second", "peak_calls_per_second");
            double minRate = GetDoubleFromEither(summary, "min_events_per_second", "min_calls_per_second");
            double stdDevRate = GetDoubleFromEither(summary, "std_dev_events_per_second", "std_dev_calls_per_second");
            double cvRate = GetDoubleFromEither(summary, "cv_events_per_second", "cv_calls_per_second");
            double avgResponseTimeMs = summary.TryGetProperty("avg_response_time_ms", out var rtMs) ? rtMs.GetDouble() : 0;
            int totalSamples = summary.TryGetProperty("total_samples", out var ts) ? ts.GetInt32() : 0;
            int totalCount = GetIntFromEither(summary, "total_events", "total_calls");

            return new ThroughputReport
            {
                TestDate = root.GetProperty("test_date").GetString() ?? "",
                SamplingIntervalMs = root.GetProperty("sampling_interval_ms").GetInt32(),
                Samples = samples,
                Summary = new ThroughputSummary
                {
                    AvgRate = avgRate,
                    PeakRate = peakRate,
                    MinRate = minRate,
                    StdDevRate = stdDevRate,
                    CvRate = cvRate,
                    AvgResponseTimeMs = avgResponseTimeMs,
                    TotalSamples = totalSamples,
                    TotalCount = totalCount
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load throughput report from {FilePath}", filePath);
            return null;
        }
    }

    private static double GetDoubleFromEither(JsonElement element, string key1, string key2)
    {
        if (element.TryGetProperty(key1, out var prop1))
            return prop1.GetDouble();
        if (element.TryGetProperty(key2, out var prop2))
            return prop2.GetDouble();
        return 0;
    }

    private static int GetIntFromEither(JsonElement element, string key1, string key2)
    {
        if (element.TryGetProperty(key1, out var prop1))
            return prop1.GetInt32();
        if (element.TryGetProperty(key2, out var prop2))
            return prop2.GetInt32();
        return 0;
    }

    private ResourceMetricsReport? LoadResourceReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse samples - handle different field names (memory_mb vs memory_rss_mb)
            var samples = new List<ResourceSampleJson>();
            if (root.TryGetProperty("samples", out var samplesArray))
            {
                foreach (var sample in samplesArray.EnumerateArray())
                {
                    var timestamp = sample.GetProperty("timestamp").GetString() ?? "";
                    var cpuPercent = sample.TryGetProperty("cpu_percent", out var cpu) ? cpu.GetDouble() : 0;
                    
                    // Get memory from memory_mb or memory_rss_mb
                    double memoryMb = 0;
                    if (sample.TryGetProperty("memory_mb", out var mem))
                        memoryMb = mem.GetDouble();
                    else if (sample.TryGetProperty("memory_rss_mb", out var rss))
                        memoryMb = rss.GetDouble();

                    samples.Add(new ResourceSampleJson
                    {
                        Timestamp = timestamp,
                        ElapsedSeconds = 0, // Not used for charting
                        CpuPercent = cpuPercent,
                        MemoryMb = memoryMb
                    });
                }
            }

            // Parse summary - handle flat structure (avg_cpu_percent, etc.)
            ResourceSummary cpuSummary;
            ResourceSummary memorySummary;

            if (root.TryGetProperty("summary", out var summary))
            {
                cpuSummary = new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_cpu_percent", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = 0, // Not in summary
                    Max = summary.TryGetProperty("peak_cpu_percent", out var peakCpu) ? peakCpu.GetDouble() : 0,
                    Mode = 0,
                    Unit = "%"
                };
                memorySummary = new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_memory_mb", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = 0, // Not in summary
                    Max = summary.TryGetProperty("peak_memory_mb", out var peakMem) ? peakMem.GetDouble() : 0,
                    Mode = 0,
                    Unit = "MB"
                };
            }
            else
            {
                // Fallback for system-metrics which may have different structure
                cpuSummary = new ResourceSummary { Avg = 0, Min = 0, Max = 0, Mode = 0, Unit = "%" };
                memorySummary = new ResourceSummary { Avg = 0, Min = 0, Max = 0, Mode = 0, Unit = "MB" };
            }

            return new ResourceMetricsReport
            {
                TestDate = root.TryGetProperty("test_date", out var td) ? td.GetString() ?? "" : "",
                SamplingIntervalMs = root.TryGetProperty("sampling_interval_ms", out var si) ? si.GetInt32() : 500,
                Samples = samples,
                CpuSummary = cpuSummary,
                MemorySummary = memorySummary
            };
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
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse samples - handle process-specific fields
            var samples = new List<ResourceSampleJson>();
            if (root.TryGetProperty("samples", out var samplesArray))
            {
                foreach (var sample in samplesArray.EnumerateArray())
                {
                    var timestamp = sample.GetProperty("timestamp").GetString() ?? "";
                    var cpuPercent = sample.TryGetProperty("cpu_percent", out var cpu) ? cpu.GetDouble() : 0;
                    
                    // Get memory from memory_rss_mb (process-specific)
                    double memoryMb = 0;
                    if (sample.TryGetProperty("memory_rss_mb", out var rss))
                        memoryMb = rss.GetDouble();
                    else if (sample.TryGetProperty("memory_mb", out var mem))
                        memoryMb = mem.GetDouble();

                    samples.Add(new ResourceSampleJson
                    {
                        Timestamp = timestamp,
                        ElapsedSeconds = 0, // Not used for charting
                        CpuPercent = cpuPercent,
                        MemoryMb = memoryMb
                    });
                }
            }

            // Parse summary - handle nested cpu_summary/memory_summary OR flat structure
            ResourceSummary cpuSummary;
            ResourceSummary memorySummary;

            if (root.TryGetProperty("cpu_summary", out var cpuSum) && root.TryGetProperty("memory_summary", out var memSum))
            {
                // Nested structure
                cpuSummary = new ResourceSummary
                {
                    Avg = cpuSum.TryGetProperty("avg", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = cpuSum.TryGetProperty("min", out var minCpu) ? minCpu.GetDouble() : 0,
                    Max = cpuSum.TryGetProperty("max", out var maxCpu) ? maxCpu.GetDouble() : 0,
                    Mode = cpuSum.TryGetProperty("mode", out var modeCpu) ? modeCpu.GetInt32() : 0,
                    Unit = cpuSum.TryGetProperty("unit", out var unitCpu) ? unitCpu.GetString() ?? "%" : "%"
                };
                memorySummary = new ResourceSummary
                {
                    Avg = memSum.TryGetProperty("avg", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = memSum.TryGetProperty("min", out var minMem) ? minMem.GetDouble() : 0,
                    Max = memSum.TryGetProperty("max", out var maxMem) ? maxMem.GetDouble() : 0,
                    Mode = memSum.TryGetProperty("mode", out var modeMem) ? modeMem.GetInt32() : 0,
                    Unit = memSum.TryGetProperty("unit", out var unitMem) ? unitMem.GetString() ?? "MB" : "MB"
                };
            }
            else if (root.TryGetProperty("summary", out var summary))
            {
                // Flat structure
                cpuSummary = new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_cpu_percent", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = 0,
                    Max = summary.TryGetProperty("peak_cpu_percent", out var peakCpu) ? peakCpu.GetDouble() : 0,
                    Mode = 0,
                    Unit = "%"
                };
                memorySummary = new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_memory_mb", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = 0,
                    Max = summary.TryGetProperty("peak_memory_mb", out var peakMem) ? peakMem.GetDouble() : 0,
                    Mode = 0,
                    Unit = "MB"
                };
            }
            else
            {
                cpuSummary = new ResourceSummary { Avg = 0, Min = 0, Max = 0, Mode = 0, Unit = "%" };
                memorySummary = new ResourceSummary { Avg = 0, Min = 0, Max = 0, Mode = 0, Unit = "MB" };
            }

            return new ResourceMetricsReport
            {
                TestDate = root.TryGetProperty("test_date", out var td) ? td.GetString() ?? "" : "",
                SamplingIntervalMs = root.TryGetProperty("sampling_interval_ms", out var si) ? si.GetInt32() : 500,
                Samples = samples,
                CpuSummary = cpuSummary,
                MemorySummary = memorySummary
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
