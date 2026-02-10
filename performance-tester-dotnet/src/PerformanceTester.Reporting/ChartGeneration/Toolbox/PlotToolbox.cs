using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.Toolbox;

/// <summary>
/// Small, pure, reusable functions for building plots.
/// Each function does one thing and has no side effects beyond the plot parameter.
/// </summary>
internal static class PlotToolbox
{
    // ═══════════════════════════════════════════════════════════════════════════
    // DATA EXTRACTION (pure functions)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts OADate timestamps from resource samples.
    /// </summary>
    public static double[] ExtractTimestamps(IReadOnlyList<ResourceSampleJson> samples)
        => samples.Select(s => s.Timestamp.ToOADate()).ToArray();

    /// <summary>
    /// Extracts OADate timestamps from throughput samples.
    /// </summary>
    public static double[] ExtractTimestamps(IReadOnlyList<ThroughputSampleJson> samples)
        => samples.Select(s => s.Timestamp.ToOADate()).ToArray();

    /// <summary>
    /// Extracts CPU% values from resource samples.
    /// </summary>
    public static double[] ExtractCpuValues(IReadOnlyList<ResourceSampleJson> samples)
        => samples.Select(s => s.CpuPercent).ToArray();

    /// <summary>
    /// Extracts Memory MB values from resource samples.
    /// </summary>
    public static double[] ExtractMemoryValues(IReadOnlyList<ResourceSampleJson> samples)
        => samples.Select(s => s.MemoryMb).ToArray();

    /// <summary>
    /// Extracts throughput rate values.
    /// </summary>
    public static double[] ExtractThroughputRates(IReadOnlyList<ThroughputSampleJson> samples)
        => samples.Select(s => s.ThroughputRate).ToArray();

    // ═══════════════════════════════════════════════════════════════════════════
    // SCATTER PLOTS (mutates plot)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a scatter plot with fill effect.
    /// </summary>
    public static void AddScatterWithFill(
        Plot plot,
        double[] timestamps,
        double[] values,
        Color color,
        float lineWidth,
        double fillAlpha,
        string legendText,
        bool useRightAxis = false)
    {
        var scatter = plot.Add.Scatter(timestamps, values);
        scatter.Color = color;
        scatter.LineWidth = lineWidth;
        scatter.FillY = true;
        scatter.FillYColor = color.WithAlpha(fillAlpha);
        scatter.LegendText = legendText;

        if (useRightAxis)
        {
            scatter.Axes.YAxis = plot.Axes.Right;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // REFERENCE LINES (mutates plot)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a horizontal average reference line.
    /// </summary>
    public static void AddAverageLine(
        Plot plot,
        double value,
        Color color,
        float lineWidth,
        bool useRightAxis = false)
    {
        var avgLine = plot.Add.HorizontalLine(value);
        avgLine.Color = color;
        avgLine.LineWidth = lineWidth;
        avgLine.LinePattern = LinePattern.Dashed;

        if (useRightAxis)
        {
            avgLine.Axes.YAxis = plot.Axes.Right;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AXIS CONFIGURATION (mutates plot)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures left axis label.
    /// </summary>
    public static void ConfigureLeftAxisLabel(Plot plot, string text, Color color, FontConfig font)
    {
        plot.Axes.Left.Label.Text = text;
        plot.Axes.Left.Label.ForeColor = color;
        plot.Axes.Left.Label.Bold = true;
        plot.Axes.Left.Label.FontSize = font.AxisLabelFontSize;
        plot.Axes.Left.Label.FontName = font.FontName;
    }

    /// <summary>
    /// Configures right axis label.
    /// </summary>
    public static void ConfigureRightAxisLabel(Plot plot, string text, Color color, FontConfig font)
    {
        plot.Axes.Right.Label.Text = text;
        plot.Axes.Right.Label.ForeColor = color;
        plot.Axes.Right.Label.Bold = true;
        plot.Axes.Right.Label.FontSize = font.AxisLabelFontSize;
        plot.Axes.Right.Label.FontName = font.FontName;
    }

    /// <summary>
    /// Configures time axis with HH:mm:ss format.
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
    /// Configures bottom axis label (X-axis) with "Time" text.
    /// </summary>
    public static void ConfigureBottomAxisLabel(Plot plot, FontConfig font)
    {
        plot.Axes.Bottom.Label.Text = "Time";
        plot.Axes.Bottom.Label.Bold = true;
        plot.Axes.Bottom.Label.FontSize = font.AxisLabelFontSize;
        plot.Axes.Bottom.Label.FontName = font.FontName;
    }


    // ═══════════════════════════════════════════════════════════════════════════
    // EMPTY PLOT CONFIGURATION (mutates plot)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets up an empty dual-axis plot with default ranges.
    /// </summary>
    public static void ConfigureEmptyDualAxisPlot(
        Plot plot,
        string leftLabel,
        string rightLabel,
        Color leftColor,
        Color rightColor,
        FontConfig font)
    {
        plot.Axes.Left.IsVisible = true;
        plot.Axes.Right.IsVisible = true;
        plot.Axes.SetLimitsY(0, 100);
        plot.Axes.Right.Min = 0;
        plot.Axes.Right.Max = 1000;

        ConfigureLeftAxisLabel(plot, leftLabel, leftColor, font);
        ConfigureRightAxisLabel(plot, rightLabel, rightColor, font);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LAYOUT & STYLING (mutates plot)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures grid styling.
    /// </summary>
    public static void ConfigureGrid(Plot plot, GridConfig config)
    {
        plot.Grid.MajorLineColor = Color.FromHex(config.LineColorHex).WithAlpha(config.LineAlpha);
    }

    /// <summary>
    /// Configures legend on right edge.
    /// </summary>
    public static void ConfigureLegend(Plot plot, FontConfig font)
    {
        plot.ShowLegend(Edge.Right);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = font.LegendFontSize;
        plot.Legend.FontName = font.FontName;
        plot.Legend.Orientation = Orientation.Vertical;
        plot.Legend.InterItemPadding = new PixelPadding(0, 0, 15, 0);
    }

    /// <summary>
    /// Configures plot layout with padding.
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
    /// Applies all standard configuration (grid, legend, layout, time axis).
    /// </summary>
    public static void ApplyStandardConfiguration(Plot plot, ChartConfig config, bool hasTitle = false)
    {
        ConfigureGrid(plot, config.Grid);
        ConfigureLegend(plot, config.Font);
        ConfigureLayout(plot, config.Dimensions, hasTitle);
        ConfigureTimeAxis(plot);
    }
}
