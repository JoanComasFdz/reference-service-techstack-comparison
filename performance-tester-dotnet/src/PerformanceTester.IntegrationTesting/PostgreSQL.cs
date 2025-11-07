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
}
