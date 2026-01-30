namespace PerformanceTester.Reporting.ChartGeneration.Toolbox;

/// <summary>
/// Pure functions for formatting legend text in plots.
/// </summary>
internal static class LegendFormatters
{
    /// <summary>
    /// Formats resource summary for legend (CPU or RAM).
    /// </summary>
    public static string FormatResourceLegend(ResourceSummary summary)
    {
        return $"Avg: {summary.Avg:F1} {summary.Unit}\n" +
               $"Min: {summary.Min:F1} {summary.Unit}\n" +
               $"Max: {summary.Max:F1} {summary.Unit}\n" +
               $"Mode: {summary.Mode} {summary.Unit}\n";
    }

    /// <summary>
    /// Formats throughput summary for legend.
    /// </summary>
    public static string FormatThroughputLegend(ThroughputSummary summary)
    {
        return $"Avg: {summary.AvgRate:F1} ({summary.AvgResponseTimeMs:F2}ms)\n" +
               $"Min: {summary.MinRate:F1}\n" +
               $"Max: {summary.PeakRate:F1}\n" +
               $"Mode: {(int)Math.Round(summary.AvgRate)}\n" +
               $"Std Dev: {summary.StdDevRate:F1}\n" +
               $"CV: {summary.CvRate:F1}%\n";
    }

    /// <summary>
    /// Creates a legend entry with title and stats.
    /// </summary>
    public static string WithTitle(string title, ResourceSummary summary)
        => $"{title}\n{FormatResourceLegend(summary)}";

    /// <summary>
    /// Creates a legend entry with title and throughput stats.
    /// </summary>
    public static string WithTitle(string title, ThroughputSummary summary)
        => $"{title}\n{FormatThroughputLegend(summary)}";
}
