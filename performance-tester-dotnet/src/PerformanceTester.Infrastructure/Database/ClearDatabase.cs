using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Clears all data from the specified database by truncating all tables.
/// Returns Unit on success, or a <see cref="ClearDatabaseError"/> describing the failure.
/// </summary>
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase(
    DatabaseName databaseName,
    CancellationToken cancellationToken = default);
