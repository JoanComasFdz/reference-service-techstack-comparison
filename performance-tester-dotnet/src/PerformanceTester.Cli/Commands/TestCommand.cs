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
using PerformanceTester.Reporting.ValueObjects;

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
            var host = context.GetHost();
            var consoleWriter = host.Services.GetRequiredService<ConsoleWriter>();

            // Parse and validate all CLI options into value objects (first failure short-circuits)
            var parseResult =
                from EventCount in EventCount.Create(context.ParseResult.GetValueForOption(eventsOption))
                from ApiWorkers in WorkerCount.Create(context.ParseResult.GetValueForOption(apiWorkersOption))
                from ApiDuration in ApiDuration.Create(context.ParseResult.GetValueForOption(apiDurationOption)!)
                from ServicePort in Port.Create(context.ParseResult.GetValueForOption(portOption))
                from WarmupEvents in WarmupEventsCount.Create(context.ParseResult.GetValueForOption(warmupEventsOption))
                from WarmupApiCalls in WarmupApiCallsCount.Create(context.ParseResult.GetValueForOption(warmupApiCallsOption))
                from DatabaseName in DatabaseName.Create(context.ParseResult.GetValueForOption(databaseOption)!)
                from InactivityTimeout in InactivityTimeout.Create(context.ParseResult.GetValueForOption(inactivityTimeoutOption)!)
                from WarmupInactivityTimeout in InactivityTimeout.Create(context.ParseResult.GetValueForOption(warmupInactivityTimeoutOption)!)
                from ResultsFolder in ResultsOutputFolder.Create(context.ParseResult.GetValueForOption(resultsFolderOption)!)
                from RabbitMqContainer in RabbitMqContainerName.Create(context.ParseResult.GetValueForOption(rabbitMqContainerOption)!)
                from PostgresContainer in PostgresContainerName.Create(context.ParseResult.GetValueForOption(postgresContainerOption)!)
                select new TestConfiguration(
                    EventCount,
                    ApiDuration,
                    ApiWorkers,
                    ServicePort,
                    WarmupEvents,
                    WarmupApiCalls,
                    DatabaseName,
                    ResultsFolder,
                    RabbitMqContainer,
                    PostgresContainer,
                    InactivityTimeout,
                    WarmupInactivityTimeout);

            if (parseResult.IsFailure)
            {
                consoleWriter.WriteError(parseResult.FailureError);
                context.ExitCode = 1;
                return;
            }

            context.ExitCode = await ExecuteAsync(
                parseResult.SuccessValue,
                host.Services.GetRequiredService<ILogger<Program>>(),
                consoleWriter,
                host.Services.GetRequiredService<ProgressReporter>(),
                host.Services.GetRequiredService<ITestOrchestrator>(),
                context.GetCancellationToken());
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        TestConfiguration config,
        ILogger<Program> logger,
        ConsoleWriter consoleWriter,
        ProgressReporter progressReporter,
        ITestOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            // Display configuration
            consoleWriter.WriteHeader("Performance Test Configuration");
            consoleWriter.WriteConfigTable(config);
            consoleWriter.WriteLine();
            consoleWriter.WriteHeader("Running Performance Test");
            progressReporter.Initialize();

            var progressAdapter = new OrchestratorProgressAdapter(
                progressReporter,
                config.EventCount,
                config.ApiDuration);

            var report = await orchestrator.RunTestAsync(
                config,
                progressAdapter,
                cancellationToken);

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

