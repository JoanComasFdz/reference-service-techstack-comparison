using Dunet;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Represents the reasons why a database could not be cleared.
/// </summary>
[Union]
public partial record ClearDatabaseError
{
    /// <summary>The specified database does not exist.</summary>
    public partial record DatabaseNotFound(DatabaseName Name);

    /// <summary>All retry attempts were exhausted due to transient failures.</summary>
    public partial record RetriesExhausted(int Attempts, Exception Last);
}
