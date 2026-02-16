using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.Common;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.ProcessFinding;
using PerformanceTester.Infrastructure.RabbitMQ;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="postgresConnectionString">PostgreSQL connection string.</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ AMQP connection string (format: amqp://user:password@host:port).</param>
    /// <param name="rabbitMqManagementPort">Optional RabbitMQ Management API port. If not specified, infers from AMQP port.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgresConnectionString,
        string rabbitMqConnectionString,
        int? rabbitMqManagementPort = null)
    {
        // Platform detection determines which static finder to use (Guideline 14: delegates for internal wiring)
        var platformResult = OSPlatformDetector.GetCurrentPlatform();

        // Unsupported platform is a deployment error — the app cannot function without a process finder
        if (platformResult.IsFailure)
        {
            throw new PlatformNotSupportedException(platformResult.FailureError);
        }

        var platform = platformResult.SuccessValue;

        // No adapter class — the closure IS the implementation (Guideline 12)
        services.AddSingleton<FindServiceProcessId>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var discoveryLogger = loggerFactory.CreateLogger(
                typeof(ServiceDiscovery).FullName!);

            // Dunet Match — exhaustive at compile time, no UnreachableException needed
            var finderLogger = platform.Match(
                linux: _ => loggerFactory.CreateLogger(typeof(LinuxProcessFinder).FullName!),
                windows: _ => loggerFactory.CreateLogger(typeof(WindowsProcessFinder).FullName!));

            var findProcessOnPort = platform.Match(
                linux: _ => (FindProcessOnPort)((p, ct) => LinuxProcessFinder.FindProcessOnPortAsync(p, finderLogger, ct)),
                windows: _ => (FindProcessOnPort)((p, ct) => WindowsProcessFinder.FindProcessOnPortAsync(p, finderLogger, ct)));

            return (port, timeout, ct) =>
                ServiceDiscovery.FindServiceProcessIdAsync(
                    port,
                    timeout,
                    findProcessOnPort,
                    discoveryLogger,
                    ct);
        });

        // DatabaseCleaner receives connection string and logger
        services.AddSingleton<IDatabase>(sp => new DatabaseCleaner(
            postgresConnectionString,
            sp.GetRequiredService<ILogger<DatabaseCleaner>>()));

        // RabbitMqCleaner receives connection string, logger, and optional management port
        services.AddSingleton<IRabbitMQ>(sp =>
            new RabbitMqCleaner(
                rabbitMqConnectionString,
                sp.GetRequiredService<ILogger<RabbitMqCleaner>>(),
                rabbitMqManagementPort));

        return services;
    }
}
