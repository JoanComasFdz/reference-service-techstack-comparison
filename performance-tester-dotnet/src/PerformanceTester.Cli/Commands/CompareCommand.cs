using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Cli.Output;
using PerformanceTester.Reporting;

namespace PerformanceTester.Cli.Commands;

/// <summary>
/// The 'compare' command generates comparison reports from test results.
/// </summary>
public static class CompareCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
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
        var consoleWriter = host.Services.GetRequiredService<IConsoleWriter>();

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

            // Load test reports
            var testReports = new List<TestReport>();
            foreach (var file in reportFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var report = JsonSerializer.Deserialize<TestReport>(json, JsonOptions);
                    if (report is not null)
                    {
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
            var comparisonGenerator = host.Services.GetRequiredService<IComparisonReportGenerator>();

            // Determine output path
            var outputPath = options.Output;
            if (string.IsNullOrWhiteSpace(outputPath) && !options.Stdout)
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                outputPath = Path.Combine(options.Folder, $"test-report-comparison-{timestamp}.md");
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
}

/// <summary>
/// Options for the compare command (mapped from CLI arguments).
/// </summary>
public sealed record CompareCommandOptions(
    string Folder,
    string? Output,
    bool Stdout);
