using JoanComasFdz.Result;
using Microsoft.Extensions.Configuration;
using PerformanceTester.Orchestration.ValueObjects;

namespace PerformanceTester.Cli.Configuration;

/// <summary>
/// Application configuration loaded from environment variables and appsettings.json.
/// Environment variables are prefixed with PERFTEST_ (e.g., PERFTEST_POSTGRES_HOST).
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>PostgreSQL connection string.</summary>
    public string PostgresConnectionString { get; init; } = string.Empty;

    /// <summary>RabbitMQ connection string (AMQP format).</summary>
    public string RabbitMqConnectionString { get; init; } = string.Empty;

    /// <summary>RabbitMQ Docker container name for monitoring.</summary>
    public required RabbitMqContainerName RabbitMqContainerName { get; init; }

    /// <summary>PostgreSQL Docker container name for monitoring.</summary>
    public required PostgresContainerName PostgresContainerName { get; init; }

    /// <summary>
    /// Loads configuration from IConfiguration (environment variables + appsettings.json).
    /// </summary>
    public static Result<AppConfiguration, string> Load(IConfiguration configuration)
    {
        // PostgreSQL configuration
        var pgHost = configuration["POSTGRES_HOST"] ?? "localhost";
        var pgPort = configuration["POSTGRES_PORT"] ?? "5432";
        var pgUser = configuration["POSTGRES_USER"] ?? "admin";
        var pgPassword = configuration["POSTGRES_PASSWORD"] ?? "admin";
        var pgDatabase = configuration["POSTGRES_DB"] ?? "postgres";

        // RabbitMQ configuration
        var rmqHost = configuration["RABBITMQ_HOST"] ?? "localhost";
        var rmqPort = configuration["RABBITMQ_PORT"] ?? "5672";
        var rmqUser = configuration["RABBITMQ_USER"] ?? "admin";
        var rmqPassword = configuration["RABBITMQ_PASS"] ?? "admin";

        // Container names (validated via value objects)
        var rmqContainerRaw = configuration["RABBITMQ_CONTAINER"] ?? "performancetest-rabbitmq";
        var pgContainerRaw = configuration["POSTGRES_CONTAINER"] ?? "performancetest-postgres";

        return
            from rmqContainer in RabbitMqContainerName.Create(rmqContainerRaw)
            from pgContainer in PostgresContainerName.Create(pgContainerRaw)
            select new AppConfiguration
            {
                PostgresConnectionString = $"Host={pgHost};Port={pgPort};Database={pgDatabase};Username={pgUser};Password={pgPassword}",
                RabbitMqConnectionString = $"amqp://{rmqUser}:{rmqPassword}@{rmqHost}:{rmqPort}",
                RabbitMqContainerName = rmqContainer,
                PostgresContainerName = pgContainer
            };
    }
}
