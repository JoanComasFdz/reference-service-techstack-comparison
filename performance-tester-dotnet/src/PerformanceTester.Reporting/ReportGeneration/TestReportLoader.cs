using System.Text.Json;
using PerformanceTester.Reporting.Shared.Utilities;
using PerformanceTester.Reporting.ValueObjects;

namespace PerformanceTester.Reporting.ReportGeneration;

/// <summary>
/// Loads test reports and their supplementary data files from a results folder.
/// Counterpart to <see cref="ReportGenerator"/> which writes these files.
/// </summary>
public static class TestReportLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new Iso8601DateTimeConverter() }
    };

    /// <summary>
    /// Supplementary file suffixes to exclude when finding main report files.
    /// </summary>
    private static readonly string[] SupplementarySuffixes =
    [
        "resource-metrics",
        "events-throughput",
        "api-throughput",
        "postgres-metrics",
        "rabbitmq-metrics",
        "system-metrics"
    ];

    /// <summary>
    /// Loads all test reports from the specified folder, including supplementary data.
    /// </summary>
    /// <param name="sourceFolder">Folder containing test report JSON files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of enriched test reports. Empty if no report files found.</returns>
    public static async Task<IReadOnlyList<TestReport>> LoadFromFolderAsync(
        ResultsSourceFolder sourceFolder,
        CancellationToken cancellationToken = default)
    {
        var folderPath = sourceFolder.Value;

        var reportFiles = Directory.GetFiles(folderPath, "test-report-*.json")
            .Where(f => !SupplementarySuffixes.Any(suffix => f.Contains(suffix)))
            .ToList();

        if (reportFiles.Count == 0)
        {
            return [];
        }

        var testReports = new List<TestReport>();
        foreach (var file in reportFiles)
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            var report = JsonSerializer.Deserialize<TestReport>(json, JsonOptions)
                ?? throw new JsonException($"Failed to deserialize report: {Path.GetFileName(file)}");

            report = await EnrichWithSupplementaryDataAsync(file, report, cancellationToken);
            testReports.Add(report);
        }

        return testReports;
    }

    private static async Task<TestReport> EnrichWithSupplementaryDataAsync(
        string mainReportPath,
        TestReport report,
        CancellationToken cancellationToken)
    {
        var basePath = mainReportPath.Replace(".json", "");

        return report with
        {
            EventsThroughputSamples = await LoadEventsThroughputAsync(
                $"{basePath}.events-throughput.json", cancellationToken),
            ApiThroughputSamples = await LoadApiThroughputAsync(
                $"{basePath}.api-throughput.json", cancellationToken),
            ProcessResourceSamples = await LoadSamplesAsync<ProcessResourceMetricsReport, ProcessResourceSample>(
                $"{basePath}.resource-metrics.json",
                r => r.Samples,
                cancellationToken),
            SystemResourceSamples = await LoadSamplesAsync<SystemMetricsReport, SystemResourceSample>(
                $"{basePath}.system-metrics.json",
                r => r.Samples,
                cancellationToken)
        };
    }

    private static async Task<IReadOnlyList<ThroughputMetricSample>> LoadEventsThroughputAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<EventsThroughputReportJson>(json, JsonOptions)
            ?? throw new JsonException($"Failed to deserialize: {Path.GetFileName(filePath)}");

        return report.Samples.Select(s => new ThroughputMetricSample
        {
            Timestamp = s.Timestamp,
            ElapsedSeconds = s.ElapsedSeconds,
            Rate = s.EventsPerSecond,
            CumulativeCount = s.TotalEvents
        }).ToArray();
    }

    private static async Task<IReadOnlyList<ThroughputMetricSample>> LoadApiThroughputAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<ApiThroughputReportJson>(json, JsonOptions)
            ?? throw new JsonException($"Failed to deserialize: {Path.GetFileName(filePath)}");

        return report.Samples.Select(s => new ThroughputMetricSample
        {
            Timestamp = s.Timestamp,
            ElapsedSeconds = s.ElapsedSeconds,
            Rate = s.CallsPerSecond,
            CumulativeCount = s.TotalCalls
        }).ToArray();
    }

    private static async Task<IReadOnlyList<TSample>> LoadSamplesAsync<TReport, TSample>(
        string filePath,
        Func<TReport, IReadOnlyList<TSample>> samplesSelector,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<TReport>(json, JsonOptions)
            ?? throw new JsonException($"Failed to deserialize: {Path.GetFileName(filePath)}");

        return samplesSelector(report);
    }
}
