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
