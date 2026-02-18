using JoanComasFdz.AssertingThat;
using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.ValueObjects;
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
    /// Asserts that the delegate finds the current process ID on the specified port.
    /// </summary>
    public static async Task FindsCurrentProcessOnPort(
        this AssertingThat<FindServiceProcessId> assertingThat,
        Port port,
        TimeSpan timeout)
    {
        var result = await assertingThat.InstanceToAssert(port, timeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(Environment.ProcessId, result.SuccessValue.Value);
    }

    /// <summary>
    /// Asserts that the delegate returns a failure when no service is running on the port.
    /// </summary>
    public static async Task FindsNoProcessOnPort(
        this AssertingThat<FindServiceProcessId> assertingThat,
        Port port,
        TimeSpan timeout)
    {
        var result = await assertingThat.InstanceToAssert(port, timeout);
        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Asserts that calling ClearDatabase with non-existent database returns DatabaseNotFound failure.
    /// </summary>
    public static async Task ClearDatabaseAsyncReturnsDatabaseNotFound(
        this AssertingThat<ClearDatabase> assertingThat,
        string databaseName)
    {
        var result = await assertingThat.InstanceToAssert(databaseName);

        var failure = Assert.IsType<Result<Unit, ClearDatabaseError>.Failure>(result);
        var error = Assert.IsType<ClearDatabaseError.DatabaseNotFound>(failure.Error);
        Assert.Equal(databaseName, error.Name);
    }

    /// <summary>
    /// Asserts that calling ClearDatabase with null returns EmptyName failure.
    /// </summary>
    public static async Task ClearDatabaseAsyncReturnsEmptyNameForNullDatabaseName(
        this AssertingThat<ClearDatabase> assertingThat)
    {
        var result = await assertingThat.InstanceToAssert(null!);

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
