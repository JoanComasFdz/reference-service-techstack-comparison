namespace PerformanceTester.Reporting;

/// <summary>
/// Generates performance visualization charts using ScottPlot.
/// Matches Python chart_generator.py functionality.
/// </summary>
public interface IChartGenerator
{
    /// <summary>
    /// Generates a PNG chart with 5 subplots showing all performance metrics.
    /// </summary>
    /// <param name="outputPath">Full path to output PNG file.</param>
    /// <param name="testReport">Complete test report data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task GenerateChartAsync(
        string outputPath,
        TestReport testReport,
        CancellationToken cancellationToken = default);
}
