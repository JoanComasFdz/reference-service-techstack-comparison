using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Cli.Configuration;
using PerformanceTester.Cli.Output;
using PerformanceTester.Orchestration;

namespace PerformanceTester.Cli.Commands;

/// <summary>
/// The 'test' command runs a complete performance test against a service.
/// </summary>
public static class TestCommand
{
    public static Command Create()
    {
        var eventsOption = new Option<int>(
            aliases: ["--events", "-e"],
            getDefaultValue: () => 10000,
            description: "Number of events to publish and consume (1-1,000,000)");

        var apiDurationOption = new Option<string>(
            aliases: ["--api-duration", "-d"],
            getDefaultValue: () => "30s",
            description: "API load test duration (e.g., 30s, 5m, 2h)");

        var apiWorkersOption = new Option<int>(
            aliases: ["--api-workers", "-w"],
            getDefaultValue: () => 1,
            description: "Concurrent virtual users for k6 (1-1000)");

        var portOption = new Option<int>(
            aliases: ["--port", "-p"],
            getDefaultValue: () => 8080,
            description: "Service port to test (1-65535)");

        var databaseOption = new Option<string>(
            aliases: ["--database", "-b"],
            getDefaultValue: () => "defaultdb",
            description: "PostgreSQL database name for the service");

        var resultsFolderOption = new Option<string>(
            aliases: ["--results-folder", "-r"],
            getDefaultValue: () => "./test-results",
            description: "Directory for test results");

        var warmupEventsOption = new Option<int>(
            aliases: ["--warmup-events"],
            getDefaultValue: () => 200,
            description: "Events for warmup phase (0-10000)");

        var warmupApiCallsOption = new Option<int>(
            aliases: ["--warmup-api-calls"],
            getDefaultValue: () => 10,
            description: "API calls during warmup (0-1000)");

        var inactivityTimeoutOption = new Option<string>(
            aliases: ["--inactivity-timeout"],
            getDefaultValue: () => "120s",
            description: "Consumer inactivity timeout (e.g., 120s, 2m)");

        var rabbitMqContainerOption = new Option<string>(
            aliases: ["--rabbitmq-container"],
            getDefaultValue: () => "performancetest-rabbitmq",
            description: "RabbitMQ Docker container name");

        var postgresContainerOption = new Option<string>(
            aliases: ["--postgres-container"],
            getDefaultValue: () => "performancetest-postgres",
            description: "PostgreSQL Docker container name");

        var command = new Command("test", "Run performance test against a service")
        {
            eventsOption,
            apiDurationOption,
            apiWorkersOption,
            portOption,
            databaseOption,
            resultsFolderOption,
            warmupEventsOption,
            warmupApiCallsOption,
            inactivityTimeoutOption,
            rabbitMqContainerOption,
            postgresContainerOption
        };

        command.SetHandler(async (InvocationContext context) =>
        {
            var events = context.ParseResult.GetValueForOption(eventsOption);
            var apiDuration = context.ParseResult.GetValueForOption(apiDurationOption)!;
            var apiWorkers = context.ParseResult.GetValueForOption(apiWorkersOption);
            var port = context.ParseResult.GetValueForOption(portOption);
            var database = context.ParseResult.GetValueForOption(databaseOption)!;
            var resultsFolder = context.ParseResult.GetValueForOption(resultsFolderOption)!;
            var warmupEvents = context.ParseResult.GetValueForOption(warmupEventsOption);
            var warmupApiCalls = context.ParseResult.GetValueForOption(warmupApiCallsOption);
            var inactivityTimeout = context.ParseResult.GetValueForOption(inactivityTimeoutOption)!;
            var rabbitMqContainer = context.ParseResult.GetValueForOption(rabbitMqContainerOption)!;
            var postgresContainer = context.ParseResult.GetValueForOption(postgresContainerOption)!;

            var host = context.GetHost();
            var cancellationToken = context.GetCancellationToken();

            var exitCode = await ExecuteAsync(
                new TestCommandOptions(
                    events,
                    apiDuration,
                    apiWorkers,
                    port,
                    database,
                    resultsFolder,
                    warmupEvents,
                    warmupApiCalls,
                    inactivityTimeout,
                    rabbitMqContainer,
                    postgresContainer),
                host,
                cancellationToken);

            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        TestCommandOptions options,
        IHost host,
        CancellationToken cancellationToken)
    {
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var consoleWriter = host.Services.GetRequiredService<ConsoleWriter>();
        var progressReporter = host.Services.GetRequiredService<ProgressReporter>();

        try
        {
            // Validate options
            var validationResult = ValidateOptions(options);
            if (validationResult is not null)
            {
                consoleWriter.WriteError(validationResult);
                return 1;
            }

            // Parse durations (already validated by ValidateOptions)
            var apiDuration = ((Result<TimeSpan, DurationParseError>.Success)DurationParser.Parse(options.ApiDuration)).Value;
            var inactivityTimeout = ((Result<TimeSpan, DurationParseError>.Success)DurationParser.Parse(options.InactivityTimeout)).Value;

            // Build configuration
            var config = new TestConfiguration(
                EventCount: options.Events,
                ApiDuration: apiDuration,
                ApiWorkers: options.ApiWorkers,
                InactivityTimeout: inactivityTimeout,
                WarmupEventCount: options.WarmupEvents,
                WarmupApiCallCount: (uint)options.WarmupApiCalls,
                ServicePort: options.Port,
                DatabaseName: options.Database,
                ResultsFolder: options.ResultsFolder,
                RabbitMqContainerName: options.RabbitMqContainer,
                PostgresContainerName: options.PostgresContainer);

            // Display configuration
            consoleWriter.WriteHeader("Performance Test Configuration");
            consoleWriter.WriteConfigTable(config);
            consoleWriter.WriteLine();

            // Ensure results folder exists
            Directory.CreateDirectory(config.ResultsFolder);

            // Get orchestrator and run test
            var orchestrator = host.Services.GetRequiredService<ITestOrchestrator>();

            consoleWriter.WriteHeader("Running Performance Test");
            progressReporter.Initialize();

            // Create progress adapter - all parameters explicit at call site
            var progressAdapter = new OrchestratorProgressAdapter(
                progressReporter: progressReporter,
                totalEventCount: config.EventCount,
                apiDuration: config.ApiDurationOrDefault);

            var report = await orchestrator.RunTestAsync(
                configuration: config,
                progress: progressAdapter,
                cancellationToken: cancellationToken);

            progressReporter.Complete();

            // Display results
            consoleWriter.WriteLine();
            consoleWriter.WriteHeader("Test Results");
            consoleWriter.WriteResultsTable(report);
            consoleWriter.WriteLine();
            consoleWriter.WriteSuccess($"Reports saved to: {config.ResultsFolder}");

            return 0;
        }
        catch (OperationCanceledException)
        {
            progressReporter.SetPhaseStatus(PhaseStatus.Cancelled);
            progressReporter.Complete();
            consoleWriter.WriteWarning("Test cancelled by user");
            return 130; // Standard exit code for SIGINT
        }
        catch (TimeoutException ex)
        {
            return HandleTestError(ex, progressReporter, consoleWriter, logger, $"Timeout: {ex.Message}", 2);
        }
        catch (InvalidOperationException ex)
        {
            return HandleTestError(ex, progressReporter, consoleWriter, logger, $"Test failed: {ex.Message}", 3);
        }
        catch (Exception ex)
        {
            return HandleTestError(ex, progressReporter, consoleWriter, logger, $"Unexpected error: {ex.Message}", 1);
        }

        static int HandleTestError(
            Exception ex,
            ProgressReporter progressReporter,
            ConsoleWriter consoleWriter,
            ILogger logger,
            string userMessage,
            int exitCode)
        {
            progressReporter.SetPhaseStatus(PhaseStatus.Failed, message: ex.Message);
            progressReporter.Complete();
            consoleWriter.WriteError(userMessage);
            logger.LogError(ex, "Test failed: {Message}", userMessage);
            return exitCode;
        }
    }

    /// <summary>
    /// Validates command options. Returns error message if invalid, null if valid.
    /// </summary>
    internal static string? ValidateOptions(TestCommandOptions options)
    {
        if (options.Events < 1 || options.Events > 1_000_000)
            return $"Events must be between 1 and 1,000,000 (got: {options.Events})";

        if (options.ApiWorkers < 1 || options.ApiWorkers > 1000)
            return $"API workers must be between 1 and 1000 (got: {options.ApiWorkers})";

        if (options.Port < 1 || options.Port > 65535)
            return $"Port must be between 1 and 65535 (got: {options.Port})";

        if (options.WarmupEvents < 0 || options.WarmupEvents > 10000)
            return $"Warmup events must be between 0 and 10000 (got: {options.WarmupEvents})";

        if (options.WarmupApiCalls < 0 || options.WarmupApiCalls > 1000)
            return $"Warmup API calls must be between 0 and 1000 (got: {options.WarmupApiCalls})";

        if (string.IsNullOrWhiteSpace(options.Database))
            return "Database name cannot be empty";

        if (!DurationParser.IsValid(options.ApiDuration))
            return $"Invalid API duration format: '{options.ApiDuration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)";

        if (!DurationParser.IsValid(options.InactivityTimeout))
            return $"Invalid inactivity timeout format: '{options.InactivityTimeout}'. Expected format: <number><unit> (e.g., 120s, 2m)";

        if (string.IsNullOrWhiteSpace(options.ResultsFolder))
            return "Results folder cannot be empty";

        if (string.IsNullOrWhiteSpace(options.RabbitMqContainer))
            return "RabbitMQ container name cannot be empty";

        if (string.IsNullOrWhiteSpace(options.PostgresContainer))
            return "PostgreSQL container name cannot be empty";

        return null;
    }
}

/// <summary>
/// Options for the test command (mapped from CLI arguments).
/// </summary>
public sealed record TestCommandOptions(
    int Events,
    string ApiDuration,
    int ApiWorkers,
    int Port,
    string Database,
    string ResultsFolder,
    int WarmupEvents,
    int WarmupApiCalls,
    string InactivityTimeout,
    string RabbitMqContainer,
    string PostgresContainer);
