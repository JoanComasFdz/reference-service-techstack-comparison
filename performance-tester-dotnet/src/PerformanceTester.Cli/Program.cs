using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.Cli.Commands;
using PerformanceTester.Cli.Configuration;
using PerformanceTester.Cli.Output;
using PerformanceTester.Orchestration;
using Serilog;

namespace PerformanceTester.Cli;

/// <summary>
/// CLI entry point for performance testing tool.
/// </summary>
public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog early for startup logging
        // Use progress-aware sink to coordinate with progress bar display
        const string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new ProgressAwareConsoleSink(outputTemplate))
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Performance Tester starting...");

            // Build command tree
            var rootCommand = BuildRootCommand();

            // Build and invoke
            var parser = new CommandLineBuilder(rootCommand)
                .UseDefaults()
                .UseHost(_ => Host.CreateDefaultBuilder(args), ConfigureHost)
                .Build();

            return await parser.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Performance testing tool for microservice comparison")
        {
            Name = "performance-tester"
        };

        // Add test command
        rootCommand.AddCommand(TestCommand.Create());

        // Add compare command
        rootCommand.AddCommand(CompareCommand.Create());

        return rootCommand;
    }

    private static void ConfigureHost(IHostBuilder hostBuilder)
    {
        hostBuilder
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddEnvironmentVariables("PERFTEST_");
            })
            .UseSerilog((context, services, loggerConfig) =>
            {
                // File sink is configured in appsettings.json
                // Console uses our progress-aware sink to coordinate with progress bar
                const string consoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
                loggerConfig
                    .ReadFrom.Configuration(context.Configuration)
                    .WriteTo.Sink(new ProgressAwareConsoleSink(consoleTemplate))
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId();
            })
            .ConfigureServices((context, services) =>
            {
                // Load configuration (fail fast on invalid env vars)
                var loadResult = AppConfiguration.Load(context.Configuration);
                if (loadResult.IsFailure)
                {
                    throw new InvalidOperationException($"Configuration error: {loadResult.FailureError}");
                }

                var appConfig = loadResult.SuccessValue;
                services.AddSingleton(appConfig);

                // Register orchestration services (unwrap value objects at boundary)
                services.AddOrchestration(
                    postgresConnectionString: appConfig.PostgresConnectionString,
                    rabbitMqConnectionString: appConfig.RabbitMqConnectionString,
                    rabbitMqContainerName: appConfig.RabbitMqContainerName.Value,
                    postgresContainerName: appConfig.PostgresContainerName.Value);

                // Register CLI-specific services
                services.AddSingleton<ConsoleWriter>();
                services.AddSingleton<ProgressReporter>();
            });
    }
}
