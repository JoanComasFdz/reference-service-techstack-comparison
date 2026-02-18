using JoanComasFdz.Result;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Clears all data from the specified database by truncating all tables.
/// Returns Unit on success, or a <see cref="ClearDatabaseError"/> describing the failure.
/// </summary>
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase(
    string databaseName,
    CancellationToken cancellationToken = default);
