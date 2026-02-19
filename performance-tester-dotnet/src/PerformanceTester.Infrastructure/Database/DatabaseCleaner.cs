using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;
using Npgsql;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, PerformanceTester.Infrastructure.Database.ClearDatabaseError>;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Pure functions for clearing PostgreSQL databases during performance testing.
/// Discovers tables dynamically and truncates with CASCADE.
/// Static class with explicit parameters (Guidelines 1, 2).
/// </summary>
internal static class DatabaseCleaner
{
    private const int MaxRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Clears all data from the specified database by truncating all tables.
    /// Accepts <see cref="DatabaseName"/> value object — name is guaranteed non-empty (Guideline 18).
    /// </summary>
    public static async Task<Result<Unit, ClearDatabaseError>> ClearDatabaseAsync(
        DatabaseName databaseName,
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("=== Clearing database: {DatabaseName} ===", databaseName);

        // Verify database exists before retrying
        cancellationToken.ThrowIfCancellationRequested();
        if (!await DatabaseExistsAsync(databaseName, connectionString, cancellationToken))
        {
            return new Failure(new ClearDatabaseError.DatabaseNotFound(databaseName));
        }

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Get connection string for target database
                var dbConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    Database = databaseName.Value
                }.ConnectionString;

                // Discover and truncate all tables
                await TruncateAllTablesAsync(dbConnectionString, logger, cancellationToken);

                logger.LogInformation("✓ Database {DatabaseName} cleared successfully", databaseName);

                return new Success(Unit.Value);
            }
            catch (Exception ex) when (attempt < MaxRetries && ex is not OperationCanceledException)
            {
                lastException = ex;
                logger.LogWarning(
                    ex,
                    "⚠️ Attempt {Attempt}/{MaxRetries} failed, retrying in {DelaySeconds}s...",
                    attempt,
                    MaxRetries,
                    RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        return new Failure(
            new ClearDatabaseError.RetriesExhausted(MaxRetries, lastException!));
    }

    private static async Task<bool> DatabaseExistsAsync(
        DatabaseName databaseName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @dbName",
            conn);
        cmd.Parameters.AddWithValue("@dbName", databaseName.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    private static async Task TruncateAllTablesAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Discover all tables in public schema
        var tables = await DiscoverTablesAsync(conn, cancellationToken);

        if (tables.Count == 0)
        {
            logger.LogDebug("No tables found (normal for empty database)");
            return;
        }

        logger.LogDebug("Discovered {TableCount} tables: {Tables}", tables.Count, string.Join(", ", tables));

        // Build TRUNCATE command for all tables with CASCADE
        // Quote identifiers to handle special characters and ensure proper SQL
        var quotedTables = tables.Select(t => $"\"{t}\"");
        var truncateCommand = $"TRUNCATE TABLE {string.Join(", ", quotedTables)} CASCADE";

        logger.LogDebug("Executing TRUNCATE command: {TruncateCommand}", truncateCommand);
        await using var cmd = new NpgsqlCommand(truncateCommand, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        logger.LogDebug("Truncated {TableCount} tables", tables.Count);
    }

    private static async Task<List<string>> DiscoverTablesAsync(
        NpgsqlConnection conn,
        CancellationToken cancellationToken)
    {
        var tables = new List<string>();

        await using var cmd = new NpgsqlCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename",
            conn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
