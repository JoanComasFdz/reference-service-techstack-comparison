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
