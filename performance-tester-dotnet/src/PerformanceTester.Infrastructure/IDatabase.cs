using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.Database;

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
    /// <returns>Success with Unit, or a ClearDatabaseError describing the failure.</returns>
    Task<Result<Unit, ClearDatabaseError>> ClearDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);
}
