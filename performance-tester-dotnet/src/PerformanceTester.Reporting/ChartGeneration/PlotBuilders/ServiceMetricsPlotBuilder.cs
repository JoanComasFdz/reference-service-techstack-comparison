using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.Toolbox;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotBuilders;

/// <summary>
/// Builds the Service CPU/RAM metrics subplot.
/// Static class - all methods are pure functions taking explicit parameters.
/// </summary>
internal static class ServiceMetricsPlotBuilder
{
    /// <summary>
    /// Builds a service metrics plot with CPU% on left axis, RAM MB on right axis.
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
            leftLabel: "Service CPU (%)",
            rightLabel: "Service RAM (MB)",
            leftColor: ChartColors.ServiceCpu,
            rightColor: ChartColors.ServiceRam,
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
        PlotToolbox.ConfigureLeftAxisLabel(plot, "Service CPU (%)", ChartColors.ServiceCpu, config.Font);

        PlotToolbox.AddScatterWithFill(
            plot,
            timestamps,
            PlotToolbox.ExtractCpuValues(data.Samples),
            color: ChartColors.ServiceCpu,
            lineWidth: config.Line.PrimaryLineWidth,
            fillAlpha: config.Line.PrimaryFillAlpha,
            legendText: LegendFormatters.WithTitle("CPU %", data.CpuSummary));

        PlotToolbox.AddAverageLine(
            plot,
            value: data.CpuSummary.Avg,
            color: ChartColors.ServiceCpuAvg,
            lineWidth: config.Line.AverageLineWidth);

        // ═══════════════════════════════════════════════════════════════════════
        // RIGHT AXIS: RAM MB
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ConfigureRightAxisLabel(plot, "Service RAM (MB)", ChartColors.ServiceRam, config.Font);

        PlotToolbox.AddScatterWithFill(
            plot,
            timestamps,
            PlotToolbox.ExtractMemoryValues(data.Samples),
            color: ChartColors.ServiceRam,
            lineWidth: config.Line.PrimaryLineWidth,
            fillAlpha: config.Line.SecondaryFillAlpha,
            legendText: LegendFormatters.WithTitle("RAM", data.MemorySummary),
            useRightAxis: true);

        PlotToolbox.AddAverageLine(
            plot,
            value: data.MemorySummary.Avg,
            color: ChartColors.ServiceRamAvg,
            lineWidth: config.Line.AverageLineWidth,
            useRightAxis: true);

        // ═══════════════════════════════════════════════════════════════════════
        // STANDARD CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ApplyStandardConfiguration(plot, config);

        return plot;
    }
}
