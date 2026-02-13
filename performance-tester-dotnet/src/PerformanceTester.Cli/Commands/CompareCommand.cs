using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Cli.Output;
using PerformanceTester.Reporting.ComparisonGeneration;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.Reporting.ValueObjects;

namespace PerformanceTester.Cli.Commands;

/// <summary>
/// The 'compare' command generates comparison reports from test results.
/// </summary>
public static class CompareCommand
{
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
            var testReports = await TestReportLoader.LoadFromFolderAsync(
                folder, cancellationToken);

            if (testReports.Count == 0)
            {
                consoleWriter.WriteWarning($"No test report files found in: {folder}");
                consoleWriter.WriteLine("Run 'performance-tester test' first to generate reports.");
                return 1;
            }

            consoleWriter.WriteInfo($"Loaded {testReports.Count} test report(s)");

            var outputPath = await comparisonGenerator.GenerateComparisonReportAsync(
                folder, testReports, cancellationToken);
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
}
