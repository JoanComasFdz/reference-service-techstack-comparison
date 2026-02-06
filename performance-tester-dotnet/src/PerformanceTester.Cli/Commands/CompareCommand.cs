using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Cli.Output;
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

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: "Output file path (default: auto-generated in results folder)");

        var stdoutOption = new Option<bool>(
            aliases: ["--stdout"],
            getDefaultValue: () => false,
            description: "Print report to stdout instead of file");

        var command = new Command("compare", "Generate comparison report from test results")
        {
            folderOption,
            outputOption,
            stdoutOption
        };

        command.SetHandler(async (InvocationContext context) =>
        {
            var folder = context.ParseResult.GetValueForOption(folderOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption);
            var stdout = context.ParseResult.GetValueForOption(stdoutOption);

            var host = context.GetHost();
            var cancellationToken = context.GetCancellationToken();

            var exitCode = await ExecuteAsync(
                new CompareCommandOptions(folder, output, stdout),
                host,
                cancellationToken);

            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        CompareCommandOptions options,
        IHost host,
        CancellationToken cancellationToken)
    {
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var consoleWriter = host.Services.GetRequiredService<ConsoleWriter>();

        try
        {
            // Validate folder exists
            if (!Directory.Exists(options.Folder))
            {
                consoleWriter.WriteError($"Results folder not found: {options.Folder}");
                return 1;
            }

            // Find test report JSON files (main reports, not supplementary)
            var reportFiles = Directory.GetFiles(options.Folder, "test-report-*.json")
                .Where(f => !f.Contains("resource-metrics")
                         && !f.Contains("events-throughput")
                         && !f.Contains("api-throughput")
                         && !f.Contains("postgres-metrics")
                         && !f.Contains("rabbitmq-metrics")
                         && !f.Contains("system-metrics"))
                .ToList();

            if (reportFiles.Count == 0)
            {
                consoleWriter.WriteWarning($"No test report files found in: {options.Folder}");
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

            // Generate comparison report
            var comparisonGenerator = host.Services.GetRequiredService<ComparisonReportGenerator>();

            // Determine output path
            var outputPath = options.Output;
            if (string.IsNullOrWhiteSpace(outputPath) && !options.Stdout)
            {
                // Use the latest test date from the reports (matches the test run timestamp)
                var latestTestDate = testReports.Max(r => r.TestDate);
                var timestamp = latestTestDate.ToString("yyyyMMdd_HHmmss");
                outputPath = Path.Combine(options.Folder, $"test-report-{timestamp}-summary.md");
            }

            if (options.Stdout)
            {
                // Generate to temp file then output to console
                var tempPath = Path.GetTempFileName();
                try
                {
                    await comparisonGenerator.GenerateComparisonReportAsync(tempPath, testReports, cancellationToken);
                    var content = await File.ReadAllTextAsync(tempPath, cancellationToken);
                    Console.WriteLine(content);
                }
                finally
                {
                    File.Delete(tempPath);
                }
            }
            else
            {
                await comparisonGenerator.GenerateComparisonReportAsync(outputPath!, testReports, cancellationToken);
                consoleWriter.WriteSuccess($"Comparison report saved to: {outputPath}");
            }

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

        var eventsThroughputSamples = await LoadThroughputSamplesAsync(
            $"{basePath}.events-throughput.json", "events_per_second", "total_events", cancellationToken);

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
    /// Loads throughput samples from events-throughput or api-throughput JSON files.
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
    /// Loads process resource samples from resource-metrics JSON file.
    /// </summary>
    private static Task<IReadOnlyList<ProcessResourceSample>> LoadProcessResourceSamplesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        return JsonFileToolbox.LoadSamplesAsync<ProcessResourceSample>(filePath, (sample, root) =>
        {
            var timestamp = DateTime.Parse(sample.GetProperty("timestamp").GetString()!);

            // Get test_date to calculate elapsed seconds
            DateTime? testDate = root.TryGetProperty("test_date", out var testDateElement)
                ? DateTime.Parse(testDateElement.GetString()!)
                : null;

            var elapsedSeconds = testDate.HasValue
                ? (timestamp - testDate.Value).TotalSeconds
                : 0.0;

            return new ProcessResourceSample
            {
                Timestamp = timestamp,
                ElapsedSeconds = elapsedSeconds,
                CpuPercent = sample.GetProperty("cpu_percent").GetDouble(),
                MemoryRssMb = sample.GetProperty("memory_rss_mb").GetDouble(),
                Threads = sample.GetProperty("threads").GetInt32()
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Loads system-wide resource samples from system-metrics JSON file.
    /// </summary>
    private static Task<IReadOnlyList<SystemResourceSample>> LoadSystemResourceSamplesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        return JsonFileToolbox.LoadSamplesAsync<SystemResourceSample>(filePath, (sample, root) =>
        {
            var timestamp = DateTime.Parse(sample.GetProperty("timestamp").GetString()!);

            // Get test_date to calculate elapsed seconds
            DateTime? testDate = root.TryGetProperty("test_date", out var testDateElement)
                ? DateTime.Parse(testDateElement.GetString()!)
                : null;

            var elapsedSeconds = testDate.HasValue
                ? (timestamp - testDate.Value).TotalSeconds
                : 0.0;

            return new SystemResourceSample
            {
                Timestamp = timestamp,
                ElapsedSeconds = elapsedSeconds,
                CpuPercent = sample.GetProperty("cpu_percent").GetDouble(),
                MemoryUsedMb = sample.GetProperty("memory_used_mb").GetDouble(),
                MemoryTotalMb = sample.GetProperty("memory_total_mb").GetDouble(),
                MemoryPercent = sample.GetProperty("memory_percent").GetDouble()
            };
        }, cancellationToken);
    }
}

/// <summary>
/// Options for the compare command (mapped from CLI arguments).
/// </summary>
public sealed record CompareCommandOptions(
    string Folder,
    string? Output,
    bool Stdout);
