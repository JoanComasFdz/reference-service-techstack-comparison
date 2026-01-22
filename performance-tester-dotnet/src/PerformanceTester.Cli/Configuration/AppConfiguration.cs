using Microsoft.Extensions.Configuration;

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
    public string RabbitMqContainerName { get; init; } = "performancetest-rabbitmq";

    /// <summary>PostgreSQL Docker container name for monitoring.</summary>
    public string PostgresContainerName { get; init; } = "performancetest-postgres";

    /// <summary>
    /// Loads configuration from IConfiguration (environment variables + appsettings.json).
    /// </summary>
    public static AppConfiguration Load(IConfiguration configuration)
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

        // Container names
        var rmqContainer = configuration["RABBITMQ_CONTAINER"] ?? "performancetest-rabbitmq";
        var pgContainer = configuration["POSTGRES_CONTAINER"] ?? "performancetest-postgres";

        return new AppConfiguration
        {
            PostgresConnectionString = $"Host={pgHost};Port={pgPort};Database={pgDatabase};Username={pgUser};Password={pgPassword}",
            RabbitMqConnectionString = $"amqp://{rmqUser}:{rmqPassword}@{rmqHost}:{rmqPort}",
            RabbitMqContainerName = rmqContainer,
            PostgresContainerName = pgContainer
        };
    }
}
