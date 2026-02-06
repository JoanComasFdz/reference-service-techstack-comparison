using JoanComasFdz.AssertingThat;
using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.IntegrationTesting;
using Xunit;

namespace PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for Infrastructure integration tests.
/// Makes tests more readable by expressing assertions in domain language.
/// </summary>
public static class InfrastructureAssertions
{

    /// <summary>
    /// Asserts that calling FindServiceProcessIdAsync with the given port throws ArgumentOutOfRangeException.
    /// </summary>
    public static AssertingThat<IServiceDiscovery> ThrowsArgumentOutOfRangeForInvalidPort(
        this AssertingThat<IServiceDiscovery> assertingThat,
        int invalidPort)
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await assertingThat.InstanceToAssert.FindServiceProcessIdAsync(
                invalidPort,
                TimeSpan.FromSeconds(1)))
            .Wait();
        return assertingThat;
    }

    /// <summary>
    /// Asserts that FindServiceProcessIdAsync finds the current process ID on the specified port.
    /// </summary>
    /// <param name="assertingThat">The AssertingThat wrapper around IServiceDiscovery</param>
    /// <param name="port">The port to check for a process</param>
    /// <param name="timeout">Maximum time to wait for process discovery</param>
    public static async Task FindsCurrentProcessOnPort(
        this AssertingThat<IServiceDiscovery> assertingThat,
        int port,
        TimeSpan timeout)
    {
        var result = await assertingThat.InstanceToAssert.FindServiceProcessIdAsync(port, timeout);

        var success = Assert.IsType<Result<int>.Success>(result);
        Assert.Equal(Environment.ProcessId, success.Value);
    }

    /// <summary>
    /// Asserts that FindServiceProcessIdAsync returns a failure when no service is running on the port.
    /// </summary>
    /// <param name="assertingThat">The AssertingThat wrapper around IServiceDiscovery</param>
    /// <param name="port">The port to check for a process</param>
    /// <param name="timeout">Maximum time to wait for process discovery</param>
    public static async Task FindsNoProcessOnPort(
        this AssertingThat<IServiceDiscovery> assertingThat,
        int port,
        TimeSpan timeout)
    {
        var result = await assertingThat.InstanceToAssert.FindServiceProcessIdAsync(port, timeout);
        Assert.IsType<Result<int>.Failure>(result);
    }

    /// <summary>
    /// Asserts that calling ClearDatabaseAsync with non-existent database returns DatabaseNotFound failure.
    /// </summary>
    public static async Task ClearDatabaseAsyncReturnsDatabaseNotFound(
        this AssertingThat<IDatabase> assertingThat,
        string databaseName)
    {
        var result = await assertingThat.InstanceToAssert.ClearDatabaseAsync(databaseName);

        var failure = Assert.IsType<Result<Unit, ClearDatabaseError>.Failure>(result);
        var error = Assert.IsType<ClearDatabaseError.DatabaseNotFound>(failure.Error);
        Assert.Equal(databaseName, error.Name);
    }

    /// <summary>
    /// Asserts that calling ClearDatabaseAsync with null returns EmptyName failure.
    /// </summary>
    public static async Task ClearDatabaseAsyncReturnsEmptyNameForNullDatabaseName(
        this AssertingThat<IDatabase> assertingThat)
    {
        var result = await assertingThat.InstanceToAssert.ClearDatabaseAsync(null!);

        var failure = Assert.IsType<Result<Unit, ClearDatabaseError>.Failure>(result);
        Assert.IsType<ClearDatabaseError.EmptyName>(failure.Error);
    }

    /// <summary>
    /// Asserts that the specified database table has no rows.
    /// </summary>
    /// <param name="assertingThat">The AssertingThat wrapper around PostgreSQL</param>
    /// <param name="dbName">The name of the database to query</param>
    /// <param name="tableName">The name of the table to check</param>
    public static async Task DatabaseTableHasNoRows(
        this AssertingThat<PostgreSQL> assertingThat,
        string dbName,
        string tableName)
    {
        var rowCount = await assertingThat.InstanceToAssert.GetTotalRowCountAsync(dbName, tableName);
        Assert.Equal(0, rowCount);
    }

    /// <summary>
    /// Asserts that the specified database table has rows (count greater than zero).
    /// </summary>
    /// <param name="assertingThat">The AssertingThat wrapper around PostgreSQL</param>
    /// <param name="dbName">The name of the database to query</param>
    /// <param name="tableName">The name of the table to check</param>
    public static async Task DatabaseTableHasRows(
        this AssertingThat<PostgreSQL> assertingThat,
        string dbName,
        string tableName)
    {
        var rowCount = await assertingThat.InstanceToAssert.GetTotalRowCountAsync(dbName, tableName);
        Assert.True(rowCount > 0);
    }

    /// <summary>
    /// Asserts that the specified RabbitMQ queue has no messages.
    /// </summary>
    /// <param name="assertingThat">The AssertingThat wrapper around RabbitMQ</param>
    /// <param name="queueName">The name of the queue to check</param>
    public static async Task QueueHasNoMessages(
        this AssertingThat<IntegrationTesting.RabbitMQ> assertingThat,
        string queueName)
    {
        var messageCount = await assertingThat.InstanceToAssert.GetQueueMessageCountAsync(queueName);
        Assert.Equal(0u, messageCount);
    }

    /// <summary>
    /// Asserts that the specified RabbitMQ queue has the expected number of messages.
    /// </summary>
    /// <param name="assertingThat">The AssertingThat wrapper around RabbitMQ</param>
    /// <param name="queueName">The name of the queue to check</param>
    /// <param name="expectedCount">The expected number of messages</param>
    public static async Task QueueHasMessageCount(
        this AssertingThat<IntegrationTesting.RabbitMQ> assertingThat,
        string queueName,
        uint expectedCount)
    {
        var messageCount = await assertingThat.InstanceToAssert.GetQueueMessageCountAsync(queueName);
        Assert.Equal(expectedCount, messageCount);
    }
}
