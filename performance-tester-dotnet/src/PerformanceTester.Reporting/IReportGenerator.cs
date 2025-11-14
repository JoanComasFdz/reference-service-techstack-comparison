namespace PerformanceTester.Reporting;

/// <summary>
/// Generates JSON test reports from performance test data.
/// Matches Python report_generator.py functionality.
/// </summary>
public interface IReportGenerator
{
    /// <summary>
    /// Generates all report files (JSON, resource metrics, throughput metrics).
    /// </summary>
    /// <param name="outputDirectory">Directory to write report files.</param>
    /// <param name="testReport">Complete test report data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task GenerateReportAsync(
        string outputDirectory,
        TestReport testReport,
        CancellationToken cancellationToken = default);
}
