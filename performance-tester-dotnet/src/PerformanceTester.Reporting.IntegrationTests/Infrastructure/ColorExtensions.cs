using ScottPlot;

namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// Extension methods for ScottPlot Color type.
/// </summary>
internal static class ColorExtensions
{
    /// <summary>
    /// Converts a ScottPlot Color to lowercase hex string format (#rrggbb).
    /// </summary>
    public static string ToLowercaseHex(this Color color)
    {
        return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
    }
}
