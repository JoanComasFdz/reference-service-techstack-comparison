using JoanComasFdz.AssertingThat;
using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests;

public sealed class DatabaseCleanerTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task ClearDatabaseAsync_WhenDatabaseHasData_ShouldTruncateAllTables()
    {
        // Arrange - Clean up any previous state, create a test table and insert data
        var testDbName = $"test_infrastructure_db_{Guid.NewGuid():N}";
        await System.PostgreSQL.DropTestDatabaseAsync(testDbName); // Ensure clean state
        await System.PostgreSQL.CreateTestDatabaseAsync(testDbName);
        await System.PostgreSQL.CreateTestTableWithDataAsync(testDbName, "test_table");

        await Asserting.That(System.PostgreSQL).DatabaseTableHasRows(testDbName, "test_table");

        // Act
        var result = await System.Infrastructure.ClearDatabase(DatabaseName.FromString(testDbName));

        // Assert
        Assert.IsType<Result<Unit, ClearDatabaseError>.Success>(result);
        await Asserting.That(System.PostgreSQL).DatabaseTableHasNoRows(testDbName, "test_table");

        // Cleanup
        await System.PostgreSQL.DropTestDatabaseAsync(testDbName);
    }

    [Fact]
    public async Task ClearDatabaseAsync_WhenDatabaseIsEmpty_ShouldSucceed()
    {
        // Arrange - Clean up any previous state
        var testDbName = $"test_empty_db_{Guid.NewGuid():N}";
        await System.PostgreSQL.DropTestDatabaseAsync(testDbName); // Ensure clean state
        await System.PostgreSQL.CreateTestDatabaseAsync(testDbName);

        // Act
        var result = await System.Infrastructure.ClearDatabase(DatabaseName.FromString(testDbName));

        // Assert
        Assert.IsType<Result<Unit, ClearDatabaseError>.Success>(result);

        // Cleanup
        await System.PostgreSQL.DropTestDatabaseAsync(testDbName);
    }

    [Fact]
    public async Task ClearDatabaseAsync_WhenDatabaseDoesNotExist_ShouldReturnDatabaseNotFound()
    {
        // Arrange
        const string nonExistentDb = "database_that_does_not_exist_12345";

        // Act & Assert
        await Asserting.That(System.Infrastructure.ClearDatabase).ClearDatabaseAsyncReturnsDatabaseNotFound(DatabaseName.FromString(nonExistentDb));
    }
}
