using System.Data;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PerformanceTester.IntegrationTesting.Logging;
using RabbitMQ.Client;

namespace PerformanceTester.IntegrationTesting.Tests;

[Collection("RabbitMQ Tests")]
public class IntegrationTestBaseTests(Xunit.Abstractions.ITestOutputHelper output) : IntegrationTestBase<System>(output)
{
    [Fact]
    public async Task ContainerManager_StartsPostgreSQLContainer_Successfully()
    {
        // Arrange - already done by base class InitializeAsync

        // Act - attempt to connect to PostgreSQL
        using var connection = new NpgsqlConnection(System.PostgreSQL.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var result = await command.ExecuteScalarAsync();

        // Assert - connection successful and can query
        Assert.NotNull(connection);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ContainerManager_StartsRabbitMQContainer_Successfully()
    {
        // Arrange - already done by base class

        // Act - attempt to connect to RabbitMQ and publish/consume message
        var factory = new ConnectionFactory
        {
            Uri = new Uri(System.RabbitMQ.ConnectionString)
        };
        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        var queueName = "test-queue-verification";
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: true,autoDelete: true, arguments: null);

        var message = "test-message";
        var body = Encoding.UTF8.GetBytes(message);
        await channel.BasicPublishAsync(exchange: "", routingKey: queueName, body: body);

        var result = await channel.BasicGetAsync(queueName, autoAck: true);

        // Assert - connection successful and can publish/consume
        Assert.NotNull(connection);
        Assert.True(connection.IsOpen);
        Assert.NotNull(result);
        Assert.Equal(message, Encoding.UTF8.GetString(result.Body.ToArray()));
    }

    [Fact]
    public void XunitLogger_WritesToTestOutput_Successfully()
    {
        // Arrange - XunitLogger is internal, so we test it via the public API
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddXunitOutput(Output));
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<IntegrationTestBaseTests>>();

        // Act & Assert - no exceptions thrown
        logger.LogInformation("Test information message");
        logger.LogWarning("Test warning message");
        logger.LogError(new Exception("Test exception"), "Test error message");
    }

    [Fact]
    public void LoggingTestExtensions_AddsXunitOutput_ToLoggingBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddXunitOutput(Output));
        var provider = services.BuildServiceProvider();

        // Act
        var logger = provider.GetRequiredService<ILogger<IntegrationTestBaseTests>>();

        // Assert
        Assert.NotNull(logger);
        logger.LogInformation("✓ Logging infrastructure configured successfully");
    }

    [Fact]
    public void IntegrationTestBase_InitializeAsync_EnsuresContainersStarted()
    {
        // Arrange/Act - base class already called InitializeAsync

        // Assert - containers are started (connection strings populated)
        Assert.NotEmpty(System.PostgreSQL.ConnectionString);
        Assert.NotEmpty(System.RabbitMQ.ConnectionString);
    }

    [Fact]
    public async Task ContainerManager_HealthChecks_PreventConnectionFailures()
    {
        // Arrange - containers started with health checks in InitializeAsync

        // Act - immediate connection attempts should succeed
        using var pgConnection = new NpgsqlConnection(
            System.PostgreSQL.ConnectionString);
        await pgConnection.OpenAsync();

        var factory = new ConnectionFactory
        {
            Uri = new Uri(System.RabbitMQ.ConnectionString)
        };
        using var rmqConnection = await factory.CreateConnectionAsync();

        // Assert - connections succeed without retry logic
        Assert.Equal(ConnectionState.Open, pgConnection.State);
        Assert.True(rmqConnection.IsOpen);
    }

    [Fact]
    public void ContainerManager_IsSingleton_AcrossMultipleCalls()
    {
        // Act
        var instance1 = ContainerManager.Instance;
        var instance2 = ContainerManager.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public async Task EnsureStartedAsync_IsIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        var manager = ContainerManager.Instance;

        // Act - call multiple times (should show "reusing" message on 2nd and 3rd calls)
        await manager.EnsureStartedAsync(Output);
        var connectionString1 = System.PostgreSQL.ConnectionString;

        await manager.EnsureStartedAsync(Output);
        var connectionString2 = System.PostgreSQL.ConnectionString;

        await manager.EnsureStartedAsync(Output);
        var connectionString3 = System.PostgreSQL.ConnectionString;

        // Assert - same connection strings (containers not restarted)
        Assert.Equal(connectionString1, connectionString2);
        Assert.Equal(connectionString2, connectionString3);
    }
}
