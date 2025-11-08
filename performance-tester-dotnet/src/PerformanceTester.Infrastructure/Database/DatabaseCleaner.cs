using Microsoft.Extensions.Logging;
using Npgsql;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Service for clearing PostgreSQL databases during performance testing.
/// Discovers tables dynamically and truncates with CASCADE.
/// </summary>
internal sealed class DatabaseCleaner(string connectionString, ILogger<DatabaseCleaner> logger) : IDatabase
{
    private readonly string _baseConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly ILogger<DatabaseCleaner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private const int MaxRetries = 2;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task ClearDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name cannot be null or empty", nameof(databaseName));
        }

        _logger.LogInformation("=== Clearing database: {DatabaseName} ===", databaseName);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Verify database exists
                if (!await DatabaseExistsAsync(databaseName, cancellationToken))
                {
                    throw new InvalidOperationException($"Database '{databaseName}' does not exist");
                }

                // Get connection string for target database
                var dbConnectionString = BuildConnectionString(databaseName);

                // Discover and truncate all tables
                await TruncateAllTablesAsync(dbConnectionString, cancellationToken);

                // Verify cleanup
                var rowCount = await GetTotalRowCountAsync(dbConnectionString, cancellationToken);
                if (rowCount > 0)
                {
                    _logger.LogWarning("⚠️ Database still has {RowCount} rows after truncation", rowCount);
                }
                else
                {
                    _logger.LogInformation("✓ Database {DatabaseName} cleared successfully", databaseName);
                }

                return; // Success
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "⚠️ Attempt {Attempt}/{MaxRetries} failed, retrying in {DelaySeconds}s...",
                    attempt, MaxRetries, _retryDelay.TotalSeconds);
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }

        // All retries exhausted
        throw new InvalidOperationException($"Failed to clear database '{databaseName}' after {MaxRetries} attempts");
    }

    private async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_baseConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @dbName",
            conn);
        cmd.Parameters.AddWithValue("@dbName", databaseName);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    private async Task TruncateAllTablesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Discover all tables in public schema
        var tables = await DiscoverTablesAsync(conn, cancellationToken);

        if (tables.Count == 0)
        {
            _logger.LogDebug("No tables found (normal for empty database)");
            return;
        }

        _logger.LogDebug("Discovered {TableCount} tables: {Tables}",
            tables.Count, string.Join(", ", tables));

        // Build TRUNCATE command for all tables with CASCADE
        // Quote identifiers to handle special characters and ensure proper SQL
        var quotedTables = tables.Select(t => $"\"{t}\"");
        var truncateCommand = $"TRUNCATE TABLE {string.Join(", ", quotedTables)} CASCADE";

        _logger.LogDebug("Executing TRUNCATE command: {TruncateCommand}", truncateCommand);
        await using var cmd = new NpgsqlCommand(truncateCommand, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Truncated {TableCount} tables", tables.Count);
    }

    private static async Task<List<string>> DiscoverTablesAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
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

    private static async Task<int> GetTotalRowCountAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Get all user tables
        var tables = await DiscoverTablesAsync(conn, cancellationToken);

        if (tables.Count == 0)
        {
            return 0;
        }

        // Count rows across all tables using actual COUNT queries
        // This is more reliable than pg_stat_user_tables which uses cached statistics
        int totalRows = 0;
        foreach (var table in tables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{table}\"", conn);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            totalRows += result != null ? Convert.ToInt32(result) : 0;
        }

        return totalRows;
    }

    private string BuildConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }
}
