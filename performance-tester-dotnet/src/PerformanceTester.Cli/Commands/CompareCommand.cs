using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Cli.Output;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ComparisonGeneration;
using PerformanceTester.Cli.Toolbox;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Cli.Commands;

/// <summary>
/// The 'compare' command generates comparison reports from test results.
/// </summary>
public static class CompareCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new Iso8601DateTimeConverter() }
    };

    public static Command Create()
    {
        var folderOption = new Option<string>(
            aliases: ["--folder", "-f"],
            getDefaultValue: () => "./test-results",
            description: "Folder containing test result JSON files");

        var command = new Command("compare", "Generate comparison report from test results")
        {
            folderOption
        };

        command.SetHandler(async (InvocationContext context) =>
        {
            var host = context.GetHost();
            var consoleWriter = host.Services.GetRequiredService<ConsoleWriter>();

            var folderResult = ResultsSourceFolder.Create(context.ParseResult.GetValueForOption(folderOption)!);
            if (folderResult.IsFailure)
            {
                consoleWriter.WriteError(folderResult.FailureError);
                context.ExitCode = 1;
                return;
            }

            context.ExitCode = await ExecuteAsync(
                folderResult.SuccessValue,
                host.Services.GetRequiredService<ILogger<Program>>(),
                consoleWriter,
                host.Services.GetRequiredService<ComparisonReportGenerator>(),
                context.GetCancellationToken());
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        ResultsSourceFolder folder,
        ILogger<Program> logger,
        ConsoleWriter consoleWriter,
        ComparisonReportGenerator comparisonGenerator,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find test report JSON files (main reports, not supplementary)
            var reportFiles = Directory.GetFiles(folder.Value, "test-report-*.json")
                .Where(f => !f.Contains("resource-metrics")
                         && !f.Contains("events-throughput")
                         && !f.Contains("api-throughput")
                         && !f.Contains("postgres-metrics")
                         && !f.Contains("rabbitmq-metrics")
                         && !f.Contains("system-metrics"))
                .ToList();

            if (reportFiles.Count == 0)
            {
                consoleWriter.WriteWarning($"No test report files found in: {folder}");
                consoleWriter.WriteLine("Run 'performance-tester test' first to generate reports.");
                return 1;
            }

            consoleWriter.WriteInfo($"Found {reportFiles.Count} test report(s)");

            // Load test reports with supplementary data
            var testReports = new List<TestReport>();
            foreach (var file in reportFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var report = JsonSerializer.Deserialize<TestReport>(json, JsonOptions);
                    if (report is not null)
                    {
                        // Load supplementary files and enrich the report
                        report = await LoadSupplementaryDataAsync(file, report, cancellationToken);
                        testReports.Add(report);
                        consoleWriter.WriteInfo($"  Loaded: {Path.GetFileName(file)}");
                    }
                }
                catch (JsonException ex)
                {
                    consoleWriter.WriteWarning($"  Skipped (invalid JSON): {Path.GetFileName(file)} - {ex.Message}");
                }
            }

            if (testReports.Count == 0)
            {
                consoleWriter.WriteError("No valid test reports could be loaded");
                return 1;
            }

            var latestTestDate = testReports.Max(r => r.TestDate);
            var timestamp = latestTestDate.ToString("yyyyMMdd_HHmmss");
            var outputPath = Path.Combine(folder.Value, $"test-report-{timestamp}-summary.md");

            await comparisonGenerator.GenerateComparisonReportAsync(outputPath, testReports, cancellationToken);
            consoleWriter.WriteSuccess($"Comparison report saved to: {outputPath}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            consoleWriter.WriteWarning("Operation cancelled by user");
            return 130;
        }
        catch (Exception ex)
        {
            consoleWriter.WriteError($"Error generating comparison: {ex.Message}");
            logger.LogError(ex, "Failed to generate comparison report");
            return 1;
        }
    }

    /// <summary>
    /// Loads supplementary data files and enriches the TestReport with sample collections.
    /// </summary>
    private static async Task<TestReport> LoadSupplementaryDataAsync(
        string mainReportPath,
        TestReport report,
        CancellationToken cancellationToken)
    {
        // Derive supplementary file paths from main report path
        // Main: test-report-{timestamp}-{service}.json
        // Supplementary: test-report-{timestamp}-{service}.{type}.json
        var basePath = mainReportPath.Replace(".json", "");

        var eventsThroughputSamples = await LoadEventsThroughputSamplesAsync(
            $"{basePath}.events-throughput.json", cancellationToken);

        var apiThroughputSamples = await LoadThroughputSamplesAsync(
            $"{basePath}.api-throughput.json", "calls_per_second", "total_calls", cancellationToken);

        var processResourceSamples = await LoadProcessResourceSamplesAsync(
            $"{basePath}.resource-metrics.json", cancellationToken);

        var systemResourceSamples = await LoadSystemResourceSamplesAsync(
            $"{basePath}.system-metrics.json", cancellationToken);

        // Return enriched report with sample data
        return report with
        {
            EventsThroughputSamples = eventsThroughputSamples,
            ApiThroughputSamples = apiThroughputSamples,
            ProcessResourceSamples = processResourceSamples,
            SystemResourceSamples = systemResourceSamples
        };
    }

    /// <summary>
    /// Loads events throughput samples via typed deserialization.
    /// </summary>
    private static async Task<IReadOnlyList<ThroughputMetricSample>> LoadEventsThroughputSamplesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var report = JsonSerializer.Deserialize<ThroughputReport>(json, JsonOptions);
            if (report is null)
                return [];

            return report.Samples.Select(s => new ThroughputMetricSample
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = s.ElapsedSeconds,
                Rate = s.EventsPerSecond,
                CumulativeCount = s.TotalEvents
            }).ToArray();
        }
        catch { return []; }
    }

    /// <summary>
    /// Loads throughput samples from api-throughput JSON files using dynamic field names.
    /// </summary>
    private static Task<IReadOnlyList<ThroughputMetricSample>> LoadThroughputSamplesAsync(
        string filePath,
        string rateFieldName,
        string countFieldName,
        CancellationToken cancellationToken)
    {
        return JsonFileToolbox.LoadSamplesAsync<ThroughputMetricSample>(filePath, (sample, root) =>
            new ThroughputMetricSample
            {
                Timestamp = DateTime.Parse(sample.GetProperty("timestamp").GetString()!),
                ElapsedSeconds = sample.GetProperty("elapsed_seconds").GetDouble(),
                Rate = sample.GetProperty(rateFieldName).GetDouble(),
                CumulativeCount = sample.GetProperty(countFieldName).GetInt32()
            }, cancellationToken);
    }

    /// <summary>
    /// Loads process resource samples via typed deserialization.
    /// </summary>
    private static async Task<IReadOnlyList<ProcessResourceSample>> LoadProcessResourceSamplesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var report = JsonSerializer.Deserialize<ProcessResourceMetricsReport>(json, JsonOptions);
            if (report is null)
                return [];

            return report.Samples;
        }
        catch { return []; }
    }

    /// <summary>
    /// Loads system-wide resource samples via typed deserialization.
    /// </summary>
    private static async Task<IReadOnlyList<SystemResourceSample>> LoadSystemResourceSamplesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var report = JsonSerializer.Deserialize<SystemMetricsReport>(json, JsonOptions);
            if (report is null)
                return [];

            return report.Samples;
        }
        catch { return []; }
    }
}
