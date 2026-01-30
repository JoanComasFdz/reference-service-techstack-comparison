using PerformanceTester.Reporting.ChartGeneration.Configuration;
using PerformanceTester.Reporting.ChartGeneration.Toolbox;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotBuilders;

/// <summary>
/// Builds throughput subplot showing events/sec and API calls/sec.
/// Static class - all methods are pure functions taking explicit parameters.
/// </summary>
internal static class ThroughputPlotBuilder
{
    /// <summary>
    /// Creates a throughput plot with events and/or API data.
    /// </summary>
    public static Plot Build(ThroughputReport? eventsData, ThroughputReport? apiData, ChartConfig config)
    {
        var plot = new Plot();

        // ═══════════════════════════════════════════════════════════════════════
        // LEFT AXIS: Throughput
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ConfigureLeftAxisLabel(
            plot,
            text: "Throughput (per second)",
            color: Colors.Black,
            font: config.Font);

        // ═══════════════════════════════════════════════════════════════════════
        // EVENTS THROUGHPUT (green theme)
        // ═══════════════════════════════════════════════════════════════════════
        if (eventsData != null && eventsData.Samples.Count > 0)
        {
            var timestamps = PlotToolbox.ExtractTimestamps(eventsData.Samples);
            var rates = PlotToolbox.ExtractThroughputRates(eventsData.Samples);

            PlotToolbox.AddScatterWithFill(
                plot,
                timestamps,
                rates,
                color: ChartColors.EventsPrimary,
                lineWidth: config.Line.PrimaryLineWidth,
                fillAlpha: config.Line.PrimaryFillAlpha,
                legendText: LegendFormatters.WithTitle("Consumed Events/sec", eventsData.Summary));

            PlotToolbox.AddAverageLine(
                plot,
                value: eventsData.Summary.AvgRate,
                color: ChartColors.EventsAverage,
                lineWidth: config.Line.AverageLineWidth);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // API THROUGHPUT (orange theme)
        // ═══════════════════════════════════════════════════════════════════════
        if (apiData != null && apiData.Samples.Count > 0)
        {
            var timestamps = PlotToolbox.ExtractTimestamps(apiData.Samples);
            var rates = PlotToolbox.ExtractThroughputRates(apiData.Samples);

            PlotToolbox.AddScatterWithFill(
                plot,
                timestamps,
                rates,
                color: ChartColors.ApiPrimary,
                lineWidth: config.Line.PrimaryLineWidth,
                fillAlpha: config.Line.PrimaryFillAlpha,
                legendText: LegendFormatters.WithTitle("API calls/sec", apiData.Summary));

            PlotToolbox.AddAverageLine(
                plot,
                value: apiData.Summary.AvgRate,
                color: ChartColors.ApiAverage,
                lineWidth: config.Line.AverageLineWidth);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // STANDARD CONFIGURATION (with title space)
        // ═══════════════════════════════════════════════════════════════════════
        PlotToolbox.ApplyStandardConfiguration(plot, config, hasTitle: true);

        return plot;
    }
}
