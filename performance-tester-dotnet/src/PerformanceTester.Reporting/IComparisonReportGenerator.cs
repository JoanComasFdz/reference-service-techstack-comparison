namespace PerformanceTester.Reporting;

/// <summary>
/// Generates Markdown comparison reports across multiple test runs.
/// Matches Python compare_test_results.py functionality.
/// </summary>
public interface IComparisonReportGenerator
{
    /// <summary>
    /// Generates a Markdown comparison report from multiple test reports.
    /// </summary>
    /// <param name="outputPath">Full path to output Markdown file.</param>
    /// <param name="testReports">Collection of test reports to compare.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task GenerateComparisonReportAsync(
        string outputPath,
        IEnumerable<TestReport> testReports,
        CancellationToken cancellationToken = default);
}
