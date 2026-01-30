# ChartGenerator Refactoring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor the monolithic `ChartGenerator` class (914 lines) into smaller, focused classes using composition, with configuration passed as readonly records.

**Architecture:** Extract 5 subplot creation responsibilities into dedicated classes (`ThroughputPlotBuilder`, `ResourcePlotBuilder`), with shared plot configuration through composition (not inheritance). Common operations (time axis, legend, grid, layout) are centralized in a `PlotConfigurator` helper.

**Tech Stack:** .NET 9, ScottPlot 5.x, SkiaSharp

---

## Analysis Summary

### Current Problems

1. **Single Responsibility Violation**: ChartGenerator handles:
   - Throughput plotting (events + API)
   - Resource plotting (4 different metric types)
   - Data loading (3 different JSON formats)
   - Image composition (bitmap combining)
   - Phase boundary/label rendering

2. **Code Duplication**:
   - Legend configuration repeated 5× (lines 251-258, 305-311, 388-396)
   - Axis label configuration repeated 8× (lines 176-179, 289-293, 295-299, 320-324, 379-383)
   - Grid configuration repeated 5× (lines 182, 302, 386)
   - Time axis configuration called 5× (lines 261, 315, 399)
   - Layout.Fixed called 5× with nearly identical padding

3. **Magic Numbers**: Font sizes (36, 26, 22), line widths (4f, 3f), paddings (100, 480, 50, 100) scattered throughout

4. **Parameter Bloat**: `CreateResourcePlot` has 7 parameters (plus implicit `this`)

### Target Structure

```
ChartGeneration/
├── ChartGenerator.cs              # Orchestrator only (~150 lines)
├── ChartColors.cs                 # (existing)
├── Configuration/
│   ├── ChartConfig.cs             # Main config record
│   ├── PlotDimensions.cs          # Width/height records
│   └── FontConfig.cs              # Font settings record
├── PlotBuilders/
│   ├── ThroughputPlotBuilder.cs   # Throughput subplot
│   └── ResourcePlotBuilder.cs     # CPU/RAM dual-axis subplot
├── PlotConfiguration/
│   └── PlotConfigurator.cs        # Shared plot setup (legend, grid, time axis)
├── DataLoading/
│   └── ChartDataLoader.cs         # JSON loading logic
└── ImageComposition/
    └── ChartImageComposer.cs      # Bitmap combining
```

---

## Task 1: Create Configuration Records

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/Configuration/ChartConfig.cs`

**Step 1: Create configuration records**

```csharp
namespace PerformanceTester.Reporting.ChartGeneration.Configuration;

/// <summary>
/// Font configuration for chart elements.
/// </summary>
public sealed record FontConfig
{
    /// <summary>
    /// Default chart font (DejaVu Sans for cross-platform consistency).
    /// </summary>
    public static readonly FontConfig Default = new();

    /// <summary>
    /// Font family name.
    /// </summary>
    public string FontName { get; init; } = "DejaVu Sans";

    /// <summary>
    /// Title font size in pixels.
    /// </summary>
    public float TitleFontSize { get; init; } = 36f;

    /// <summary>
    /// Axis label font size in pixels.
    /// </summary>
    public float AxisLabelFontSize { get; init; } = 26f;

    /// <summary>
    /// Legend font size in pixels.
    /// </summary>
    public float LegendFontSize { get; init; } = 22f;

    /// <summary>
    /// Phase label font size in pixels.
    /// </summary>
    public float PhaseLabelFontSize { get; init; } = 22f;
}

/// <summary>
/// Line styling configuration.
/// </summary>
public sealed record LineConfig
{
    /// <summary>
    /// Default line configuration.
    /// </summary>
    public static readonly LineConfig Default = new();

    /// <summary>
    /// Primary data line width.
    /// </summary>
    public float PrimaryLineWidth { get; init; } = 4f;

    /// <summary>
    /// Average/reference line width.
    /// </summary>
    public float AverageLineWidth { get; init; } = 3f;

    /// <summary>
    /// Phase boundary line width.
    /// </summary>
    public float BoundaryLineWidth { get; init; } = 3f;

    /// <summary>
    /// Fill alpha for primary lines (0-1).
    /// </summary>
    public double PrimaryFillAlpha { get; init; } = 0.3;

    /// <summary>
    /// Fill alpha for secondary lines like RAM (0-1).
    /// </summary>
    public double SecondaryFillAlpha { get; init; } = 0.2;
}

/// <summary>
/// Plot dimension configuration.
/// </summary>
public sealed record PlotDimensions
{
    /// <summary>
    /// Default plot dimensions.
    /// </summary>
    public static readonly PlotDimensions Default = new();

    /// <summary>
    /// Plot width in pixels.
    /// </summary>
    public int Width { get; init; } = 2400;

    /// <summary>
    /// Standard subplot height in pixels.
    /// </summary>
    public int Height { get; init; } = 480;

    /// <summary>
    /// Throughput plot height (taller for title and 2 legend items).
    /// </summary>
    public int ThroughputHeight { get; init; } = 630;

    /// <summary>
    /// Left padding in pixels.
    /// </summary>
    public int PaddingLeft { get; init; } = 100;

    /// <summary>
    /// Right padding in pixels (space for legend).
    /// </summary>
    public int PaddingRight { get; init; } = 480;

    /// <summary>
    /// Bottom padding in pixels.
    /// </summary>
    public int PaddingBottom { get; init; } = 50;

    /// <summary>
    /// Top padding for standard plots.
    /// </summary>
    public int PaddingTop { get; init; } = 50;

    /// <summary>
    /// Top padding for title plot (extra space for title).
    /// </summary>
    public int PaddingTopWithTitle { get; init; } = 100;
}

/// <summary>
/// Grid configuration.
/// </summary>
public sealed record GridConfig
{
    /// <summary>
    /// Default grid configuration.
    /// </summary>
    public static readonly GridConfig Default = new();

    /// <summary>
    /// Grid line color hex code.
    /// </summary>
    public string LineColorHex { get; init; } = "#cccccc";

    /// <summary>
    /// Grid line alpha (0-1).
    /// </summary>
    public double LineAlpha { get; init; } = 0.3;
}

/// <summary>
/// Combined chart configuration.
/// </summary>
public sealed record ChartConfig
{
    /// <summary>
    /// Default chart configuration.
    /// </summary>
    public static readonly ChartConfig Default = new();

    /// <summary>
    /// Font settings.
    /// </summary>
    public FontConfig Font { get; init; } = FontConfig.Default;

    /// <summary>
    /// Line styling settings.
    /// </summary>
    public LineConfig Line { get; init; } = LineConfig.Default;

    /// <summary>
    /// Plot dimension settings.
    /// </summary>
    public PlotDimensions Dimensions { get; init; } = PlotDimensions.Default;

    /// <summary>
    /// Grid settings.
    /// </summary>
    public GridConfig Grid { get; init; } = GridConfig.Default;
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/Configuration/
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add configuration records for chart styling

Extract magic numbers (font sizes, line widths, dimensions, padding)
into immutable record types with sensible defaults.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Create PlotConfigurator for Shared Plot Setup

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/PlotConfiguration/PlotConfigurator.cs`

**Step 1: Create PlotConfigurator**

```csharp
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;

/// <summary>
/// Configures common plot elements (legend, grid, time axis, layout).
/// Used by all plot builders to ensure consistent styling.
/// </summary>
internal static class PlotConfigurator
{
    /// <summary>
    /// Configures the time axis to display HH:mm:ss format.
    /// </summary>
    public static void ConfigureTimeAxis(Plot plot)
    {
        var tickGen = new ScottPlot.TickGenerators.DateTimeAutomatic
        {
            LabelFormatter = dt => dt.ToString("HH:mm:ss")
        };
        plot.Axes.Bottom.TickGenerator = tickGen;
    }

    /// <summary>
    /// Configures the grid with standard styling.
    /// </summary>
    public static void ConfigureGrid(Plot plot, GridConfig config)
    {
        plot.Grid.MajorLineColor = Color.FromHex(config.LineColorHex).WithAlpha(config.LineAlpha);
    }

    /// <summary>
    /// Configures the legend on the right edge of the plot.
    /// </summary>
    public static void ConfigureLegend(Plot plot, FontConfig fontConfig)
    {
        plot.ShowLegend(Edge.Right);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = fontConfig.LegendFontSize;
        plot.Legend.FontName = fontConfig.FontName;
        plot.Legend.Orientation = Orientation.Vertical;
        plot.Legend.InterItemPadding = new PixelPadding(0, 0, 15, 0);
    }

    /// <summary>
    /// Configures the plot layout with standard padding.
    /// </summary>
    public static void ConfigureLayout(Plot plot, PlotDimensions dimensions, bool hasTitle = false)
    {
        var topPadding = hasTitle ? dimensions.PaddingTopWithTitle : dimensions.PaddingTop;
        plot.Layout.Fixed(new PixelPadding(
            left: dimensions.PaddingLeft,
            right: dimensions.PaddingRight,
            bottom: dimensions.PaddingBottom,
            top: topPadding));
    }

    /// <summary>
    /// Configures a Y-axis label with standard styling.
    /// </summary>
    public static void ConfigureAxisLabel(
        IAxis axis,
        string text,
        Color color,
        FontConfig fontConfig)
    {
        axis.Label.Text = text;
        axis.Label.ForeColor = color;
        axis.Label.Bold = true;
        axis.Label.FontSize = fontConfig.AxisLabelFontSize;
        axis.Label.FontName = fontConfig.FontName;
    }

    /// <summary>
    /// Configures the bottom axis label (X-axis) with "Time" text.
    /// </summary>
    public static void ConfigureBottomAxisLabel(Plot plot, FontConfig fontConfig)
    {
        plot.Axes.Bottom.Label.Text = "Time";
        plot.Axes.Bottom.Label.Bold = true;
        plot.Axes.Bottom.Label.FontSize = fontConfig.AxisLabelFontSize;
        plot.Axes.Bottom.Label.FontName = fontConfig.FontName;
    }

    /// <summary>
    /// Configures tick label rotation for bottom axis.
    /// </summary>
    public static void ConfigureTickLabelRotation(Plot plot)
    {
        plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleLeft;
    }

    /// <summary>
    /// Applies all standard configuration to a plot.
    /// </summary>
    public static void ApplyStandardConfiguration(Plot plot, ChartConfig config, bool hasTitle = false)
    {
        ConfigureGrid(plot, config.Grid);
        ConfigureLegend(plot, config.Font);
        ConfigureLayout(plot, config.Dimensions, hasTitle);
        ConfigureTimeAxis(plot);
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/PlotConfiguration/
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add PlotConfigurator for shared plot setup

Centralize common plot configuration (grid, legend, time axis, layout)
into a static helper class. Eliminates code duplication across plot builders.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Create ThroughputPlotBuilder

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/PlotBuilders/ThroughputPlotBuilder.cs`

**Step 1: Create ThroughputPlotBuilder**

```csharp
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotBuilders;

/// <summary>
/// Builds throughput subplot showing events/sec and API calls/sec.
/// </summary>
internal sealed class ThroughputPlotBuilder
{
    private readonly ChartConfig _config;

    public ThroughputPlotBuilder(ChartConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Creates a throughput plot with events and/or API data.
    /// </summary>
    public Plot Build(ThroughputReport? eventsData, ThroughputReport? apiData)
    {
        var plot = new Plot();

        // Configure left axis
        PlotConfigurator.ConfigureAxisLabel(
            plot.Axes.Left,
            "Throughput (per second)",
            Colors.Black,
            _config.Font);

        // Plot data series
        if (eventsData != null && eventsData.Samples.Count > 0)
        {
            PlotEventsThroughput(plot, eventsData);
        }

        if (apiData != null && apiData.Samples.Count > 0)
        {
            PlotApiThroughput(plot, apiData);
        }

        // Apply standard configuration (with title space)
        PlotConfigurator.ApplyStandardConfiguration(plot, _config, hasTitle: true);

        return plot;
    }

    private void PlotEventsThroughput(Plot plot, ThroughputReport data)
    {
        var timestamps = data.Samples
            .Select(s => DateTime.Parse(s.Timestamp).ToOADate())
            .ToArray();
        var rates = data.Samples
            .Select(s => s.ThroughputRate)
            .ToArray();

        // Primary line with fill
        var scatter = plot.Add.Scatter(timestamps, rates);
        scatter.Color = ChartColors.EventsPrimary;
        scatter.LineWidth = _config.Line.PrimaryLineWidth;
        scatter.FillY = true;
        scatter.FillYColor = ChartColors.EventsPrimary.WithAlpha(_config.Line.PrimaryFillAlpha);
        scatter.LegendText = "Consumed Events/sec\n" + FormatThroughputLegend(data.Summary);

        // Average line
        var avgLine = plot.Add.HorizontalLine(data.Summary.AvgRate);
        avgLine.Color = ChartColors.EventsAverage;
        avgLine.LineWidth = _config.Line.AverageLineWidth;
        avgLine.LinePattern = LinePattern.Dashed;
    }

    private void PlotApiThroughput(Plot plot, ThroughputReport data)
    {
        var timestamps = data.Samples
            .Select(s => DateTime.Parse(s.Timestamp).ToOADate())
            .ToArray();
        var rates = data.Samples
            .Select(s => s.ThroughputRate)
            .ToArray();

        // Primary line with fill
        var scatter = plot.Add.Scatter(timestamps, rates);
        scatter.Color = ChartColors.ApiPrimary;
        scatter.LineWidth = _config.Line.PrimaryLineWidth;
        scatter.FillY = true;
        scatter.FillYColor = ChartColors.ApiPrimary.WithAlpha(_config.Line.PrimaryFillAlpha);
        scatter.LegendText = "API calls/sec\n" + FormatThroughputLegend(data.Summary);

        // Average line
        var avgLine = plot.Add.HorizontalLine(data.Summary.AvgRate);
        avgLine.Color = ChartColors.ApiAverage;
        avgLine.LineWidth = _config.Line.AverageLineWidth;
        avgLine.LinePattern = LinePattern.Dashed;
    }

    private static string FormatThroughputLegend(ThroughputSummary summary)
    {
        return $"Avg: {summary.AvgRate:F1} ({summary.AvgResponseTimeMs:F2}ms)\n" +
               $"Min: {summary.MinRate:F1}\n" +
               $"Max: {summary.PeakRate:F1}\n" +
               $"Mode: {(int)Math.Round(summary.AvgRate)}\n" +
               $"Std Dev: {summary.StdDevRate:F1}\n" +
               $"CV: {summary.CvRate:F1}%\n";
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/PlotBuilders/ThroughputPlotBuilder.cs
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add ThroughputPlotBuilder

Extract throughput plotting logic (events/sec, API calls/sec) into
dedicated builder class. Uses ChartConfig for styling and PlotConfigurator
for shared setup.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Create ResourcePlotBuilder

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/PlotBuilders/ResourcePlotBuilder.cs`

**Step 1: Create ResourcePlotBuilder**

```csharp
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotBuilders;

/// <summary>
/// Configuration for a resource plot's color scheme.
/// </summary>
internal sealed record ResourcePlotColors(
    Color CpuPrimary,
    Color CpuAverage,
    Color RamPrimary,
    Color RamAverage);

/// <summary>
/// Builds resource subplot showing CPU% and RAM (dual Y-axes).
/// Used for Service, RabbitMQ, PostgreSQL, and System metrics.
/// </summary>
internal sealed class ResourcePlotBuilder
{
    private readonly ChartConfig _config;

    public ResourcePlotBuilder(ChartConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Creates a resource plot with CPU and RAM data.
    /// </summary>
    public Plot Build(
        ResourceMetricsReport? data,
        string cpuLabel,
        string ramLabel,
        ResourcePlotColors colors)
    {
        var plot = new Plot();

        if (data == null || data.Samples.Count == 0)
        {
            return BuildEmptyPlot(plot, cpuLabel, ramLabel, colors);
        }

        return BuildPopulatedPlot(plot, data, cpuLabel, ramLabel, colors);
    }

    private Plot BuildEmptyPlot(
        Plot plot,
        string cpuLabel,
        string ramLabel,
        ResourcePlotColors colors)
    {
        // Explicitly enable axes for empty plots
        plot.Axes.Left.IsVisible = true;
        plot.Axes.Right.IsVisible = true;

        // Set default axis limits
        plot.Axes.SetLimitsY(0, 100);
        plot.Axes.Right.Min = 0;
        plot.Axes.Right.Max = 1000;

        // Configure axis labels
        PlotConfigurator.ConfigureAxisLabel(plot.Axes.Left, cpuLabel, colors.CpuPrimary, _config.Font);
        PlotConfigurator.ConfigureAxisLabel(plot.Axes.Right, ramLabel, colors.RamPrimary, _config.Font);

        // Apply standard configuration
        PlotConfigurator.ApplyStandardConfiguration(plot, _config);

        return plot;
    }

    private Plot BuildPopulatedPlot(
        Plot plot,
        ResourceMetricsReport data,
        string cpuLabel,
        string ramLabel,
        ResourcePlotColors colors)
    {
        var timestamps = data.Samples
            .Select(s => DateTime.Parse(s.Timestamp).ToOADate())
            .ToArray();

        // LEFT AXIS: CPU %
        PlotConfigurator.ConfigureAxisLabel(plot.Axes.Left, cpuLabel, colors.CpuPrimary, _config.Font);
        PlotCpuData(plot, timestamps, data, colors);

        // RIGHT AXIS: RAM MB
        PlotConfigurator.ConfigureAxisLabel(plot.Axes.Right, ramLabel, colors.RamPrimary, _config.Font);
        PlotRamData(plot, timestamps, data, colors);

        // Apply standard configuration
        PlotConfigurator.ApplyStandardConfiguration(plot, _config);

        return plot;
    }

    private void PlotCpuData(
        Plot plot,
        double[] timestamps,
        ResourceMetricsReport data,
        ResourcePlotColors colors)
    {
        var cpuValues = data.Samples.Select(s => s.CpuPercent).ToArray();

        // Primary line with fill
        var scatter = plot.Add.Scatter(timestamps, cpuValues);
        scatter.Color = colors.CpuPrimary;
        scatter.LineWidth = _config.Line.PrimaryLineWidth;
        scatter.FillY = true;
        scatter.FillYColor = colors.CpuPrimary.WithAlpha(_config.Line.PrimaryFillAlpha);
        scatter.LegendText = "CPU %\n" + FormatResourceLegend(data.CpuSummary);

        // Average line
        var avgLine = plot.Add.HorizontalLine(data.CpuSummary.Avg);
        avgLine.Color = colors.CpuAverage;
        avgLine.LineWidth = _config.Line.AverageLineWidth;
        avgLine.LinePattern = LinePattern.Dashed;
    }

    private void PlotRamData(
        Plot plot,
        double[] timestamps,
        ResourceMetricsReport data,
        ResourcePlotColors colors)
    {
        var ramValues = data.Samples.Select(s => s.MemoryMb).ToArray();

        // Primary line with fill (using right axis)
        var scatter = plot.Add.Scatter(timestamps, ramValues);
        scatter.Color = colors.RamPrimary;
        scatter.LineWidth = _config.Line.PrimaryLineWidth;
        scatter.Axes.YAxis = plot.Axes.Right;
        scatter.FillY = true;
        scatter.FillYColor = colors.RamPrimary.WithAlpha(_config.Line.SecondaryFillAlpha);
        scatter.LegendText = "RAM\n" + FormatResourceLegend(data.MemorySummary);

        // Average line (using right axis)
        var avgLine = plot.Add.HorizontalLine(data.MemorySummary.Avg);
        avgLine.Color = colors.RamAverage;
        avgLine.LineWidth = _config.Line.AverageLineWidth;
        avgLine.LinePattern = LinePattern.Dashed;
        avgLine.Axes.YAxis = plot.Axes.Right;
    }

    private static string FormatResourceLegend(ResourceSummary summary)
    {
        return $"Avg: {summary.Avg:F1} {summary.Unit}\n" +
               $"Min: {summary.Min:F1} {summary.Unit}\n" +
               $"Max: {summary.Max:F1} {summary.Unit}\n" +
               $"Mode: {summary.Mode} {summary.Unit}\n";
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/PlotBuilders/ResourcePlotBuilder.cs
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add ResourcePlotBuilder for CPU/RAM subplots

Extract resource plotting logic (dual Y-axis for CPU% and RAM) into
dedicated builder. Uses ResourcePlotColors record to pass color schemes
without excessive parameters.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Create ChartDataLoader

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/DataLoading/ChartDataLoader.cs`

**Step 1: Create ChartDataLoader**

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.Reporting.ChartGeneration.DataLoading;

/// <summary>
/// Loads chart data from JSON files.
/// </summary>
internal sealed class ChartDataLoader
{
    private readonly ILogger _logger;

    public ChartDataLoader(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads throughput report from JSON file.
    /// Handles both events and API throughput formats.
    /// </summary>
    public ThroughputReport? LoadThroughputReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var samples = ParseThroughputSamples(root);
            var summary = ParseThroughputSummary(root);

            return new ThroughputReport
            {
                TestDate = root.GetProperty("test_date").GetString() ?? "",
                SamplingIntervalMs = root.GetProperty("sampling_interval_ms").GetInt32(),
                Samples = samples,
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load throughput report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads resource metrics report from JSON file.
    /// Used for RabbitMQ, PostgreSQL, and system metrics.
    /// </summary>
    public ResourceMetricsReport? LoadResourceReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var samples = ParseResourceSamples(root);
            var (cpuSummary, memorySummary) = ParseResourceSummary(root, samples);

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

    /// <summary>
    /// Loads process resource metrics report from JSON file.
    /// Used for service (monitored process) metrics.
    /// </summary>
    public ResourceMetricsReport? LoadProcessResourceReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var samples = ParseProcessResourceSamples(root);
            var (cpuSummary, memorySummary) = ParseProcessResourceSummary(root, samples);

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

    #region Throughput Parsing

    private static List<ThroughputSampleJson> ParseThroughputSamples(JsonElement root)
    {
        var samples = new List<ThroughputSampleJson>();
        if (!root.TryGetProperty("samples", out var samplesArray))
            return samples;

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

        return samples;
    }

    private static ThroughputSummary ParseThroughputSummary(JsonElement root)
    {
        var summary = root.GetProperty("summary");

        return new ThroughputSummary
        {
            AvgRate = GetDoubleFromEither(summary, "avg_events_per_second", "avg_calls_per_second"),
            PeakRate = GetDoubleFromEither(summary, "peak_events_per_second", "peak_calls_per_second"),
            MinRate = GetDoubleFromEither(summary, "min_events_per_second", "min_calls_per_second"),
            StdDevRate = GetDoubleFromEither(summary, "std_dev_events_per_second", "std_dev_calls_per_second"),
            CvRate = GetDoubleFromEither(summary, "cv_events_per_second", "cv_calls_per_second"),
            AvgResponseTimeMs = summary.TryGetProperty("avg_response_time_ms", out var rtMs) ? rtMs.GetDouble() : 0,
            TotalSamples = summary.TryGetProperty("total_samples", out var ts) ? ts.GetInt32() : 0,
            TotalCount = GetIntFromEither(summary, "total_events", "total_calls")
        };
    }

    #endregion

    #region Resource Parsing

    private static List<ResourceSampleJson> ParseResourceSamples(JsonElement root)
    {
        var samples = new List<ResourceSampleJson>();
        if (!root.TryGetProperty("samples", out var samplesArray))
            return samples;

        foreach (var sample in samplesArray.EnumerateArray())
        {
            var timestamp = sample.GetProperty("timestamp").GetString() ?? "";
            var cpuPercent = sample.TryGetProperty("cpu_percent", out var cpu) ? cpu.GetDouble() : 0;

            // Get memory from memory_mb, memory_rss_mb, or memory_used_mb
            double memoryMb = 0;
            if (sample.TryGetProperty("memory_mb", out var mem))
                memoryMb = mem.GetDouble();
            else if (sample.TryGetProperty("memory_rss_mb", out var rss))
                memoryMb = rss.GetDouble();
            else if (sample.TryGetProperty("memory_used_mb", out var used))
                memoryMb = used.GetDouble();

            samples.Add(new ResourceSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = 0,
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            });
        }

        return samples;
    }

    private static (ResourceSummary cpu, ResourceSummary memory) ParseResourceSummary(
        JsonElement root,
        List<ResourceSampleJson> samples)
    {
        var cpuValues = samples.Select(s => s.CpuPercent).ToList();
        var memoryValues = samples.Select(s => s.MemoryMb).ToList();

        if (root.TryGetProperty("summary", out var summary))
        {
            var cpuSummary = new ResourceSummary
            {
                Avg = summary.TryGetProperty("avg_cpu_percent", out var avgCpu) ? avgCpu.GetDouble() : 0,
                Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                Max = summary.TryGetProperty("peak_cpu_percent", out var peakCpu) ? peakCpu.GetDouble() : 0,
                Mode = CalculateMode(cpuValues),
                Unit = "%"
            };

            double avgMemory = 0;
            if (summary.TryGetProperty("avg_memory_mb", out var avgMem))
                avgMemory = avgMem.GetDouble();
            else if (summary.TryGetProperty("avg_memory_used_mb", out var avgUsed))
                avgMemory = avgUsed.GetDouble();

            double peakMemory = 0;
            if (summary.TryGetProperty("peak_memory_mb", out var peakMem))
                peakMemory = peakMem.GetDouble();
            else if (summary.TryGetProperty("peak_memory_used_mb", out var peakUsed))
                peakMemory = peakUsed.GetDouble();

            var memorySummary = new ResourceSummary
            {
                Avg = avgMemory,
                Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                Max = peakMemory,
                Mode = CalculateMode(memoryValues),
                Unit = "MB"
            };

            return (cpuSummary, memorySummary);
        }

        // Fallback
        return (
            new ResourceSummary
            {
                Avg = 0,
                Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(cpuValues),
                Unit = "%"
            },
            new ResourceSummary
            {
                Avg = 0,
                Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(memoryValues),
                Unit = "MB"
            }
        );
    }

    private static List<ResourceSampleJson> ParseProcessResourceSamples(JsonElement root)
    {
        var samples = new List<ResourceSampleJson>();
        if (!root.TryGetProperty("samples", out var samplesArray))
            return samples;

        foreach (var sample in samplesArray.EnumerateArray())
        {
            var timestamp = sample.GetProperty("timestamp").GetString() ?? "";
            var cpuPercent = sample.TryGetProperty("cpu_percent", out var cpu) ? cpu.GetDouble() : 0;

            double memoryMb = 0;
            if (sample.TryGetProperty("memory_rss_mb", out var rss))
                memoryMb = rss.GetDouble();
            else if (sample.TryGetProperty("memory_mb", out var mem))
                memoryMb = mem.GetDouble();

            samples.Add(new ResourceSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = 0,
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            });
        }

        return samples;
    }

    private static (ResourceSummary cpu, ResourceSummary memory) ParseProcessResourceSummary(
        JsonElement root,
        List<ResourceSampleJson> samples)
    {
        var cpuValues = samples.Select(s => s.CpuPercent).ToList();
        var memoryValues = samples.Select(s => s.MemoryMb).ToList();

        if (root.TryGetProperty("cpu_summary", out var cpuSum) && root.TryGetProperty("memory_summary", out var memSum))
        {
            return (
                new ResourceSummary
                {
                    Avg = cpuSum.TryGetProperty("avg", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = cpuSum.TryGetProperty("max", out var maxCpu) ? maxCpu.GetDouble() : 0,
                    Mode = CalculateMode(cpuValues),
                    Unit = cpuSum.TryGetProperty("unit", out var unitCpu) ? unitCpu.GetString() ?? "%" : "%"
                },
                new ResourceSummary
                {
                    Avg = memSum.TryGetProperty("avg", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = memSum.TryGetProperty("max", out var maxMem) ? maxMem.GetDouble() : 0,
                    Mode = CalculateMode(memoryValues),
                    Unit = memSum.TryGetProperty("unit", out var unitMem) ? unitMem.GetString() ?? "MB" : "MB"
                }
            );
        }

        if (root.TryGetProperty("summary", out var summary))
        {
            return (
                new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_cpu_percent", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = summary.TryGetProperty("peak_cpu_percent", out var peakCpu) ? peakCpu.GetDouble() : 0,
                    Mode = CalculateMode(cpuValues),
                    Unit = "%"
                },
                new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_memory_rss_mb", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = summary.TryGetProperty("peak_memory_rss_mb", out var peakMem) ? peakMem.GetDouble() : 0,
                    Mode = CalculateMode(memoryValues),
                    Unit = "MB"
                }
            );
        }

        // Fallback
        return (
            new ResourceSummary
            {
                Avg = 0,
                Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(cpuValues),
                Unit = "%"
            },
            new ResourceSummary
            {
                Avg = 0,
                Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(memoryValues),
                Unit = "MB"
            }
        );
    }

    #endregion

    #region Helpers

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

    private static int CalculateMode(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        return values
            .Select(v => (int)Math.Round(v))
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }

    #endregion
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/DataLoading/
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add ChartDataLoader for JSON parsing

Extract JSON data loading logic into dedicated class. Handles three
different JSON formats: throughput, resource metrics, and process
resource metrics.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Create ChartImageComposer

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/ImageComposition/ChartImageComposer.cs`

**Step 1: Create ChartImageComposer**

```csharp
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;
using SkiaSharp;

namespace PerformanceTester.Reporting.ChartGeneration.ImageComposition;

/// <summary>
/// Composes multiple plots into a single vertically-stacked image.
/// </summary>
internal sealed class ChartImageComposer
{
    private readonly PlotDimensions _dimensions;

    public ChartImageComposer(PlotDimensions dimensions)
    {
        _dimensions = dimensions;
    }

    /// <summary>
    /// Renders a plot to a bitmap with the specified height.
    /// </summary>
    public SKBitmap RenderPlotToBitmap(Plot plot, int height)
    {
        var image = plot.GetImage(_dimensions.Width, height);
        return SKBitmap.Decode(image.GetImageBytes());
    }

    /// <summary>
    /// Combines multiple bitmaps vertically and saves to file.
    /// </summary>
    public void CombineAndSave(IReadOnlyList<SKBitmap> bitmaps, string outputPath)
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

    /// <summary>
    /// Disposes all bitmaps in the list.
    /// </summary>
    public static void DisposeBitmaps(IReadOnlyList<SKBitmap> bitmaps)
    {
        foreach (var bitmap in bitmaps)
        {
            bitmap.Dispose();
        }
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/ImageComposition/
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add ChartImageComposer for bitmap operations

Extract bitmap rendering and vertical stacking logic into dedicated
class. Handles plot-to-bitmap conversion and multi-image composition.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Create PhaseOverlayRenderer

**Files:**
- Create: `src/PerformanceTester.Reporting/ChartGeneration/PlotConfiguration/PhaseOverlayRenderer.cs`

**Step 1: Create PhaseOverlayRenderer**

```csharp
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;

/// <summary>
/// Renders phase boundaries and labels on plots.
/// </summary>
internal sealed class PhaseOverlayRenderer
{
    private readonly ChartConfig _config;

    public PhaseOverlayRenderer(ChartConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Adds phase boundary vertical lines to a plot.
    /// </summary>
    public void AddPhaseBoundaries(Plot plot, TestReport testReport)
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
            vline.LineWidth = _config.Line.BoundaryLineWidth;
        }
    }

    /// <summary>
    /// Adds phase labels to the top plot (throughput plot).
    /// </summary>
    public void AddPhaseLabels(Plot plot, TestReport testReport, double yMax)
    {
        var phase1Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase1Start);
        var phase2End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase2End);
        var phase3Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3Start);
        var phase3End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3End);

        var labelYPosition = yMax * 0.95;

        AddConsumeLabel(plot, phase1Start, phase2End, testReport.Configuration.NumEvents, labelYPosition);
        AddApiLabel(plot, phase3Start, phase3End, testReport.Configuration, labelYPosition);
    }

    /// <summary>
    /// Adds title to the top plot.
    /// </summary>
    public void AddTitle(Plot plot, TestReport testReport)
    {
        var humanDate = testReport.TestDate.ToString("MMMM dd, yyyy");
        var humanTime = testReport.TestDate.ToString("HH:mm:ss");
        var title = $"Performance Metrics - {testReport.MonitoredProcess.Name} - {humanDate} at {humanTime}";

        plot.Axes.Title.Label.Text = title;
        plot.Axes.Title.Label.FontSize = _config.Font.TitleFontSize;
        plot.Axes.Title.Label.FontName = _config.Font.FontName;
        plot.Axes.Title.Label.Bold = true;
    }

    private void AddConsumeLabel(Plot plot, DateTime phase1Start, DateTime phase2End, int numEvents, double yPosition)
    {
        var midTime = phase1Start.AddTicks((phase2End - phase1Start).Ticks / 2);
        var label = numEvents > 0 ? $"Consume: {numEvents}" : "Consume";

        var text = plot.Add.Text(label, midTime.ToOADate(), yPosition);
        text.LabelFontColor = ChartColors.ConsumePhase;
        text.LabelFontSize = _config.Font.PhaseLabelFontSize;
        text.LabelFontName = _config.Font.FontName;
        text.LabelBold = true;
        text.LabelAlignment = Alignment.UpperCenter;
    }

    private void AddApiLabel(Plot plot, DateTime phase3Start, DateTime phase3End, TestConfiguration config, double yPosition)
    {
        var midTime = phase3Start.AddTicks((phase3End - phase3Start).Ticks / 2);
        var label = "API";

        if (!string.IsNullOrEmpty(config.ApiDuration))
        {
            label = config.ApiConcurrentWorkers > 0
                ? $"API: {config.ApiDuration} ({config.ApiConcurrentWorkers}w)"
                : $"API: {config.ApiDuration}";
        }

        var text = plot.Add.Text(label, midTime.ToOADate(), yPosition);
        text.LabelFontColor = ChartColors.ApiPhase;
        text.LabelFontSize = _config.Font.PhaseLabelFontSize;
        text.LabelFontName = _config.Font.FontName;
        text.LabelBold = true;
        text.LabelAlignment = Alignment.UpperCenter;
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/PlotConfiguration/PhaseOverlayRenderer.cs
git commit -m "$(cat <<'EOF'
feat(ChartGeneration): add PhaseOverlayRenderer for phase markers

Extract phase boundary and label rendering into dedicated class.
Handles vertical lines, text labels for Consume and API phases, and
chart title.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Refactor ChartGenerator to Use New Components

**Files:**
- Modify: `src/PerformanceTester.Reporting/ChartGeneration/ChartGenerator.cs`

**Step 1: Rewrite ChartGenerator as orchestrator**

Replace the entire contents of ChartGenerator.cs with:

```csharp
using Microsoft.Extensions.Logging;
using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.DataLoading;
using PerformanceTester.Reporting.ChartGeneration.ImageComposition;
using PerformanceTester.Reporting.ChartGeneration.PlotBuilders;
using PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;
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
    private readonly ChartDataLoader _dataLoader;
    private readonly ThroughputPlotBuilder _throughputBuilder;
    private readonly ResourcePlotBuilder _resourceBuilder;
    private readonly PhaseOverlayRenderer _phaseRenderer;
    private readonly ChartImageComposer _imageComposer;

    public ChartGenerator(ILogger<ChartGenerator> logger)
        : this(logger, ChartConfig.Default)
    {
    }

    public ChartGenerator(ILogger<ChartGenerator> logger, ChartConfig config)
    {
        _logger = logger;
        _config = config;
        _dataLoader = new ChartDataLoader(logger);
        _throughputBuilder = new ThroughputPlotBuilder(config);
        _resourceBuilder = new ResourcePlotBuilder(config);
        _phaseRenderer = new PhaseOverlayRenderer(config);
        _imageComposer = new ChartImageComposer(config.Dimensions);
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
        var eventsData = _dataLoader.LoadThroughputReport(dataFiles.EventsThroughput);
        var apiData = _dataLoader.LoadThroughputReport(dataFiles.ApiThroughput);
        var serviceData = _dataLoader.LoadProcessResourceReport(dataFiles.ResourceMetrics);
        var rabbitmqData = _dataLoader.LoadResourceReport(dataFiles.RabbitmqMetrics);
        var postgresData = _dataLoader.LoadResourceReport(dataFiles.PostgresMetrics);
        var systemData = _dataLoader.LoadResourceReport(dataFiles.SystemMetrics);

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
            _imageComposer.CombineAndSave(bitmaps, outputPath);
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
            _throughputBuilder.Build(eventsData, apiData),
            _resourceBuilder.Build(serviceData, "Service CPU (%)", "Service RAM (MB)",
                new ResourcePlotColors(ChartColors.ServiceCpu, ChartColors.ServiceCpuAvg,
                    ChartColors.ServiceRam, ChartColors.ServiceRamAvg)),
            _resourceBuilder.Build(rabbitmqData, "RabbitMQ CPU (%)", "RabbitMQ RAM (MB)",
                new ResourcePlotColors(ChartColors.RabbitMqCpu, ChartColors.RabbitMqCpuAvg,
                    ChartColors.RabbitMqRam, ChartColors.RabbitMqRamAvg)),
            _resourceBuilder.Build(postgresData, "PostgreSQL CPU (%)", "PostgreSQL RAM (MB)",
                new ResourcePlotColors(ChartColors.PostgresCpu, ChartColors.PostgresCpuAvg,
                    ChartColors.PostgresRam, ChartColors.PostgresRamAvg)),
            _resourceBuilder.Build(systemData, "Overall System CPU (%)", "Overall System RAM (MB)",
                new ResourcePlotColors(ChartColors.SystemCpu, ChartColors.SystemCpuAvg,
                    ChartColors.SystemRam, ChartColors.SystemRamAvg))
        ];
    }

    private void AddPhaseOverlays(List<Plot> plots, TestReport testReport)
    {
        foreach (var plot in plots)
        {
            _phaseRenderer.AddPhaseBoundaries(plot, testReport);
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
        _phaseRenderer.AddPhaseLabels(plot, testReport, yMax);
        _phaseRenderer.AddTitle(plot, testReport);
    }

    private void ConfigureBottomPlot(Plot plot)
    {
        PlotConfigurator.ConfigureBottomAxisLabel(plot, _config.Font);
        PlotConfigurator.ConfigureTickLabelRotation(plot);
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
            .Select((p, i) => _imageComposer.RenderPlotToBitmap(
                p,
                i == 0 ? _config.Dimensions.ThroughputHeight : _config.Dimensions.Height))
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
```

**Step 2: Verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Expected: Build succeeded

**Step 3: Run all ChartGenerator tests**

Run: `dotnet test src/PerformanceTester.Reporting.IntegrationTests --filter "FullyQualifiedName~ChartGenerator" --no-build -v q`
Expected: All 11 tests pass

**Step 4: Commit**

```bash
git add src/PerformanceTester.Reporting/ChartGeneration/ChartGenerator.cs
git commit -m "$(cat <<'EOF'
refactor(ChartGeneration): rewrite ChartGenerator as orchestrator

Replace 914-line monolithic class with ~200-line orchestrator that
delegates to:
- ThroughputPlotBuilder: events/API throughput plotting
- ResourcePlotBuilder: CPU/RAM dual-axis plotting
- ChartDataLoader: JSON file parsing
- ChartImageComposer: bitmap rendering/combining
- PhaseOverlayRenderer: phase boundaries and labels
- PlotConfigurator: shared plot configuration

Configuration is passed via immutable ChartConfig record.
No test changes required - public API unchanged.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Run Full Test Suite and Verify

**Step 1: Run all tests**

Run: `dotnet test src/PerformanceTester.sln --no-build -v q`
Expected: All tests pass (100+)

**Step 2: Build in Release mode**

Run: `dotnet build src/PerformanceTester.sln -c Release -v q`
Expected: Build succeeded, 0 warnings

**Step 3: Verify CLI runs (Manual - user responsibility)**

Note: The CLI test against the running dotnet9aot service requires WSL2 network access. Since the devcontainer cannot reach services running on the WSL2 host, this step must be performed by the user:

```bash
# From WSL2 (not devcontainer):
cd /workspace/performance-tester-dotnet/src/PerformanceTester.Cli
dotnet run -- test --port 8093 --database dotnet9_db --events 1000 --api-duration 5s
```

**Step 4: Commit any fixes if needed**

If tests fail, fix and commit:

```bash
git add -A
git commit -m "fix(ChartGeneration): address test failures after refactoring

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 10: Final Cleanup and Summary

**Step 1: Verify no unused code remains**

Run: `dotnet build src/PerformanceTester.Reporting -v q`
Check for: Any warnings about unused variables/methods

**Step 2: Verify file structure**

Expected structure after refactoring:

```
src/PerformanceTester.Reporting/ChartGeneration/
├── ChartGenerator.cs                           # ~200 lines (was 914)
├── ChartColors.cs                              # Unchanged
├── Configuration/
│   └── ChartConfig.cs                          # ~120 lines
├── DataLoading/
│   └── ChartDataLoader.cs                      # ~250 lines
├── ImageComposition/
│   └── ChartImageComposer.cs                   # ~60 lines
├── PlotBuilders/
│   ├── ThroughputPlotBuilder.cs                # ~100 lines
│   └── ResourcePlotBuilder.cs                  # ~130 lines
└── PlotConfiguration/
    ├── PlotConfigurator.cs                     # ~90 lines
    └── PhaseOverlayRenderer.cs                 # ~100 lines
```

**Step 3: Final commit (if any remaining changes)**

```bash
git status
# If clean, skip
# If changes:
git add -A
git commit -m "chore(ChartGeneration): final cleanup after refactoring

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Summary

### What Changed

| Before | After |
|--------|-------|
| 1 file, 914 lines | 8 files, ~1050 lines total |
| Magic numbers scattered | Configuration records with defaults |
| Code duplication (5× legend config, 8× axis config) | Single PlotConfigurator |
| 7-parameter CreateResourcePlot | ResourcePlotColors record |
| Mixed responsibilities | Single Responsibility per class |

### Files Created

1. `Configuration/ChartConfig.cs` - Immutable config records
2. `PlotConfiguration/PlotConfigurator.cs` - Shared plot setup
3. `PlotConfiguration/PhaseOverlayRenderer.cs` - Phase markers
4. `PlotBuilders/ThroughputPlotBuilder.cs` - Throughput subplot
5. `PlotBuilders/ResourcePlotBuilder.cs` - Resource subplot
6. `DataLoading/ChartDataLoader.cs` - JSON parsing
7. `ImageComposition/ChartImageComposer.cs` - Bitmap operations

### Public API

**Unchanged** - IChartGenerator interface and ChartGenerator constructor signature preserved. Tests should pass without modification.

### CLI Testing Note

The CLI integration test (`dotnet run -- test --port 8093 ...`) requires network access to the dotnet9aot service running on WSL2 host. This cannot be performed from within the devcontainer. The user must run this test manually from their WSL2 environment.
