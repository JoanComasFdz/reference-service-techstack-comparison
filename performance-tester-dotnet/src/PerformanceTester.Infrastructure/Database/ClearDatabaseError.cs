using Dunet;

namespace PerformanceTester.Infrastructure.Database;

/// <summary>
/// Represents the reasons why a database could not be cleared.
/// </summary>
[Union]
public partial record ClearDatabaseError
{
    /// <summary>Database name was null, empty, or whitespace.</summary>
    public partial record EmptyName;

    /// <summary>The specified database does not exist.</summary>
    public partial record DatabaseNotFound(string Name);

    /// <summary>All retry attempts were exhausted due to transient failures.</summary>
    public partial record RetriesExhausted(int Attempts, Exception Last);
}
