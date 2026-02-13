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
    /// All values are validated through value objects at this ingress point.
    /// </summary>
    public static Result<AppConfiguration, string> Load(IConfiguration configuration)
    {
        return
            // PostgreSQL
            from pgHost in Host.Create(configuration["POSTGRES_HOST"] ?? "localhost")
            from pgPort in Port.Create(configuration["POSTGRES_PORT"] ?? "5432")
            from pgUser in Username.Create(configuration["POSTGRES_USER"] ?? "admin")
            from pgPassword in Password.Create(configuration["POSTGRES_PASSWORD"] ?? "admin")
            from pgDatabase in DatabaseName.Create(configuration["POSTGRES_DB"] ?? "postgres")
                // RabbitMQ
            from rmqHost in Host.Create(configuration["RABBITMQ_HOST"] ?? "localhost")
            from rmqPort in Port.Create(configuration["RABBITMQ_PORT"] ?? "5672")
            from rmqUser in Username.Create(configuration["RABBITMQ_USER"] ?? "admin")
            from rmqPassword in Password.Create(configuration["RABBITMQ_PASS"] ?? "admin")
                // Container names
            from rmqContainer in RabbitMqContainerName.Create(configuration["RABBITMQ_CONTAINER"] ?? "performancetest-rabbitmq")
            from pgContainer in PostgresContainerName.Create(configuration["POSTGRES_CONTAINER"] ?? "performancetest-postgres")
            select new AppConfiguration
            {
                PostgresConnectionString = $"Host={pgHost.Value};Port={pgPort.Value};Database={pgDatabase.Value};Username={pgUser.Value};Password={pgPassword.Value}",
                RabbitMqConnectionString = $"amqp://{rmqUser.Value}:{rmqPassword.Value}@{rmqHost.Value}:{rmqPort.Value}",
                RabbitMqContainerName = rmqContainer,
                PostgresContainerName = pgContainer
            };
    }
}
