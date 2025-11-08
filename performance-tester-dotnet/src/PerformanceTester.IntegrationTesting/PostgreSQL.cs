using Npgsql;

namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Represents the PostgreSQL database part of the system.
/// </summary>
/// <param name="ConnectionString"></param>
public class PostgreSQL(string ConnectionString)
{
    /// <summary>
    /// The connection string used to connect to the PostgreSQL database.
    /// <para>
    /// Use it for your system under test configuration so that it can connect to PostgreSQL.
    /// </para
    /// </summary>
    public string ConnectionString { get; } = ConnectionString;

    /// <summary>
    /// Creates a new PostgreSQL database with the specified name.
    /// </summary>
    /// <param name="dbName">The name of the database to create</param>
    public async Task CreateTestDatabaseAsync(string dbName)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {dbName}", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Drops a PostgreSQL database with the specified name.
    /// Terminates all active connections to the database before dropping it.
    /// </summary>
    /// <param name="dbName">The name of the database to drop</param>
    public async Task DropTestDatabaseAsync(string dbName)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Terminate connections first
        await using var terminateCmd = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}'",
            conn);
        await terminateCmd.ExecuteNonQueryAsync();

        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName}", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Creates a test table with sample data in the specified database.
    /// The table has columns: id (SERIAL PRIMARY KEY), name (TEXT).
    /// Inserts three test records: 'test1', 'test2', 'test3'.
    /// </summary>
    /// <param name="dbName">The name of the database where the table should be created</param>
    /// <param name="tableName">The name of the table to create</param>
    public async Task CreateTestTableWithDataAsync(string dbName, string tableName)
    {
        // Build connection string properly using NpgsqlConnectionStringBuilder
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = dbName
        };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        // Drop table if it exists (clean state)
        await using var dropCmd = new NpgsqlCommand(
            $"DROP TABLE IF EXISTS {tableName}",
            conn);
        await dropCmd.ExecuteNonQueryAsync();

        // Create table
        await using var createCmd = new NpgsqlCommand(
            $"CREATE TABLE {tableName} (id SERIAL PRIMARY KEY, name TEXT)",
            conn);
        await createCmd.ExecuteNonQueryAsync();

        // Insert data
        await using var insertCmd = new NpgsqlCommand(
            $"INSERT INTO {tableName} (name) VALUES ('test1'), ('test2'), ('test3')",
            conn);
        await insertCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Gets the total row count from the specified table in the specified database.
    /// </summary>
    /// <param name="dbName">The name of the database to query</param>
    /// <param name="tableName">The name of the table to count rows from</param>
    /// <returns>The number of rows in the specified table</returns>
    public async Task<int> GetTotalRowCountAsync(string dbName, string tableName)
    {
        // Build connection string properly using NpgsqlConnectionStringBuilder
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = dbName
        };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        // Use direct COUNT query instead of statistics (more reliable for tests)
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName}",
            conn);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
