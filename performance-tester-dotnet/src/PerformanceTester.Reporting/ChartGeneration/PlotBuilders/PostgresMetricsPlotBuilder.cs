using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.Toolbox;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotBuilders;

/// <summary>
/// Builds the PostgreSQL CPU/RAM metrics subplot.
/// Static class - all methods are pure functions taking explicit parameters.
/// </summary>
internal static class PostgresMetricsPlotBuilder
{
    /// <summary>
    /// Builds a PostgreSQL metrics plot with CPU% on left axis, RAM MB on right axis.
    /// </summary>
    public static Plot Build(ResourceMetricsReport? data, ChartConfig config)
    {
        var plot = new Plot();

        if (data == null || data.Samples.Count == 0)
        {
            return BuildEmptyPlot(plot, config);
        }

        return BuildPopulatedPlot(plot, data, config);
    }

    private static Plot BuildEmptyPlot(Plot plot, ChartConfig config)
    {
        PlotToolbox.ConfigureEmptyDualAxisPlot(
            plot,
            leftLabel: "PostgreSQL CPU (%)",
            rightLabel: "PostgreSQL RAM (MB)",
            leftColor: ChartColors.PostgresCpu,
            rightColor: ChartColors.PostgresRam,
            font: config.Font);

        PlotToolbox.ApplyStandardConfiguration(plot, config);
        return plot;
    }

    private static Plot BuildPopulatedPlot(Plot plot, ResourceMetricsReport data, ChartConfig config)
    {
        var timestamps = PlotToolbox.ExtractTimestamps(data.Samples);

        // ═══════════════════════════════════════════════════════════════════════
        // LEFT AXIS: CPU %
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ConfigureLeftAxisLabel(plot, "PostgreSQL CPU (%)", ChartColors.PostgresCpu, config.Font);

        PlotToolbox.AddScatterWithFill(
            plot,
            timestamps,
            PlotToolbox.ExtractCpuValues(data.Samples),
            color: ChartColors.PostgresCpu,
            lineWidth: config.Line.PrimaryLineWidth,
            fillAlpha: config.Line.PrimaryFillAlpha,
            legendText: LegendFormatters.WithTitle("CPU %", data.CpuSummary));

        PlotToolbox.AddAverageLine(
            plot,
            value: data.CpuSummary.Avg,
            color: ChartColors.PostgresCpuAvg,
            lineWidth: config.Line.AverageLineWidth);

        // ═══════════════════════════════════════════════════════════════════════
        // RIGHT AXIS: RAM MB
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ConfigureRightAxisLabel(plot, "PostgreSQL RAM (MB)", ChartColors.PostgresRam, config.Font);

        PlotToolbox.AddScatterWithFill(
            plot,
            timestamps,
            PlotToolbox.ExtractMemoryValues(data.Samples),
            color: ChartColors.PostgresRam,
            lineWidth: config.Line.PrimaryLineWidth,
            fillAlpha: config.Line.SecondaryFillAlpha,
            legendText: LegendFormatters.WithTitle("RAM", data.MemorySummary),
            useRightAxis: true);

        PlotToolbox.AddAverageLine(
            plot,
            value: data.MemorySummary.Avg,
            color: ChartColors.PostgresRamAvg,
            lineWidth: config.Line.AverageLineWidth,
            useRightAxis: true);

        // ═══════════════════════════════════════════════════════════════════════
        // STANDARD CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ApplyStandardConfiguration(plot, config);

        return plot;
    }
}
