namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Utility class for formatting chart legend text.
/// Produces consistent legend formatting matching Python matplotlib output.
/// </summary>
public static class ChartLegendFormatter
{
    /// <summary>
    /// Formats a throughput legend with performance statistics.
    /// Includes average throughput, response time, min/max values, mode, standard deviation, and CV%.
    /// </summary>
    /// <param name="prefix">The metric prefix (e.g., "Events", "API")</param>
    /// <param name="avg">Average throughput rate</param>
    /// <param name="responseTimeMs">Average response time in milliseconds</param>
    /// <param name="min">Minimum throughput rate</param>
    /// <param name="max">Maximum throughput rate</param>
    /// <param name="mode">Mode (most common value)</param>
    /// <param name="stdDev">Standard deviation</param>
    /// <param name="cv">Coefficient of variation as percentage</param>
    /// <returns>Formatted legend text with newlines</returns>
    public static string FormatThroughputLegend(
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

    /// <summary>
    /// Formats a resource usage legend with statistics.
    /// Includes average, min, max, and mode values with units (%, MB, etc.).
    /// </summary>
    /// <param name="avg">Average resource usage</param>
    /// <param name="min">Minimum resource usage</param>
    /// <param name="max">Maximum resource usage</param>
    /// <param name="mode">Mode (most common value)</param>
    /// <param name="unit">Unit of measurement (%, MB, etc.)</param>
    /// <returns>Formatted legend text with newlines</returns>
    public static string FormatResourceLegend(
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
