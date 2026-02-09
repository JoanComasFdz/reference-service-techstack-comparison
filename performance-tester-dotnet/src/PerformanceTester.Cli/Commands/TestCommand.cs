using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Cli.Output;
using PerformanceTester.Orchestration;
using PerformanceTester.Orchestration.ValueObjects;

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

        var warmupInactivityTimeoutOption = new Option<string>(
            aliases: ["--warmup-inactivity-timeout"],
            getDefaultValue: () => "30s",
            description: "Warmup consumer inactivity timeout (e.g., 30s, 1m)");

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
            warmupInactivityTimeoutOption,
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
            var warmupInactivityTimeout = context.ParseResult.GetValueForOption(warmupInactivityTimeoutOption)!;
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
                    warmupInactivityTimeout,
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
            // Parse value objects (first failure short-circuits)
            var parseResult =
                from ec in EventCount.Create(options.Events)
                from aw in WorkerCount.Create(options.ApiWorkers)
                from ad in ApiDuration.Create(options.ApiDuration)
                from sp in Port.Create(options.Port)
                from we in WarmupEventsCount.Create(options.WarmupEvents)
                from wa in WarmupApiCallsCount.Create(options.WarmupApiCalls)
                from db in DatabaseName.Create(options.Database)
                from it in InactivityTimeout.Create(options.InactivityTimeout)
                from wt in InactivityTimeout.Create(options.WarmupInactivityTimeout)
                from rf in ResultsFolder.Create(options.ResultsFolder)
                from rc in ContainerName.Create(options.RabbitMqContainer, "RabbitMQ")
                from pc in ContainerName.Create(options.PostgresContainer, "PostgreSQL")
                select (EventCount: ec, ApiWorkers: aw, ApiDuration: ad, ServicePort: sp,
                        WarmupEvents: we, WarmupApiCalls: wa, DatabaseName: db,
                        InactivityTimeout: it, WarmupInactivityTimeout: wt, ResultsFolder: rf,
                        RabbitMqContainer: rc, PostgresContainer: pc);

            if (parseResult.IsFailure)
            {
                consoleWriter.WriteError(parseResult.FailureError);
                return 1;
            }

            // Build configuration
            var config = new TestConfiguration(
                parseResult.SuccessValue.EventCount,
                parseResult.SuccessValue.ApiDuration,
                parseResult.SuccessValue.ApiWorkers,
                parseResult.SuccessValue.ServicePort,
                parseResult.SuccessValue.WarmupEvents,
                parseResult.SuccessValue.WarmupApiCalls,
                parseResult.SuccessValue.DatabaseName,
                parseResult.SuccessValue.ResultsFolder,
                parseResult.SuccessValue.RabbitMqContainer,
                parseResult.SuccessValue.PostgresContainer,
                parseResult.SuccessValue.InactivityTimeout,
                parseResult.SuccessValue.WarmupInactivityTimeout);

            // Display configuration
            consoleWriter.WriteHeader("Performance Test Configuration");
            consoleWriter.WriteConfigTable(config);
            consoleWriter.WriteLine();

            // Ensure results folder exists
            Directory.CreateDirectory(config.ResultsFolder.Value);

            // Get orchestrator and run test
            var orchestrator = host.Services.GetRequiredService<ITestOrchestrator>();

            consoleWriter.WriteHeader("Running Performance Test");
            progressReporter.Initialize();

            // Create progress adapter - all parameters explicit at call site
            var progressAdapter = new OrchestratorProgressAdapter(
                progressReporter: progressReporter,
                totalEventCount: config.EventCount.Value,
                apiDuration: config.ApiDuration.Value);

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
    string WarmupInactivityTimeout,
    string RabbitMqContainer,
    string PostgresContainer);
