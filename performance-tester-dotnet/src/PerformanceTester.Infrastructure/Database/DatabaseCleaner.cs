using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using Npgsql;
using static JoanComasFdz.Result.Result<JoanComasFdz.Result.Unit, PerformanceTester.Infrastructure.Database.ClearDatabaseError>;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Service for clearing PostgreSQL databases during performance testing.
/// Discovers tables dynamically and truncates with CASCADE.
/// </summary>
internal sealed class DatabaseCleaner(string connectionString, ILogger<DatabaseCleaner> logger)
{
    private readonly string _baseConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly ILogger<DatabaseCleaner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private const int MaxRetries = 2;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Clears all data from the specified database by truncating all tables.
    /// </summary>
    public async Task<Result<Unit, ClearDatabaseError>> ClearDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return new Failure(new ClearDatabaseError.EmptyName());
        }

        _logger.LogInformation("=== Clearing database: {DatabaseName} ===", databaseName);

        // Verify database exists before retrying
        cancellationToken.ThrowIfCancellationRequested();
        if (!await DatabaseExistsAsync(databaseName, cancellationToken))
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
                var dbConnectionString = new NpgsqlConnectionStringBuilder(_baseConnectionString)
                {
                    Database = databaseName
                }.ConnectionString;

                // Discover and truncate all tables
                await TruncateAllTablesAsync(dbConnectionString, cancellationToken);

                _logger.LogInformation("✓ Database {DatabaseName} cleared successfully", databaseName);

                return new Success(Unit.Value);
            }
            catch (Exception ex) when (attempt < MaxRetries && ex is not OperationCanceledException)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "⚠️ Attempt {Attempt}/{MaxRetries} failed, retrying in {DelaySeconds}s...",
                    attempt,
                    MaxRetries,
                    _retryDelay.TotalSeconds);
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }

        return new Failure(
            new ClearDatabaseError.RetriesExhausted(MaxRetries, lastException!));
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

        _logger.LogDebug("Discovered {TableCount} tables: {Tables}", tables.Count, string.Join(", ", tables));

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
}
