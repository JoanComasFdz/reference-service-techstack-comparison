namespace PerformanceTester.Infrastructure;

/// <summary>
/// Service for managing PostgreSQL databases during testing.
/// </summary>
public interface IDatabase
{
    /// <summary>
    /// Clears all data from the specified database by truncating all tables.
    /// </summary>
    /// <param name="databaseName">Name of the database to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Database name is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Database clearing failed after retries.</exception>
    Task ClearDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);
}
