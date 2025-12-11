namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Base system class that provides per-test RabbitMQ vhost isolation.
/// Each test gets its own vhost, ensuring complete event isolation.
/// </summary>
/// <remarks>
/// <para>
/// When using this base class, each test will:
/// 1. Create a unique vhost before initialization
/// 2. Set permissions for the test user on the vhost
/// 3. Use a connection string with the vhost included
/// 4. Delete the vhost after the test completes
/// </para>
/// <para>
/// This eliminates cross-test contamination by providing complete namespace isolation
/// for exchanges, queues, bindings, and connections.
/// </para>
/// </remarks>
public class VhostIsolatedSystem : System
{
    /// <summary>
    /// The unique vhost name for this test instance.
    /// Format: test_{testClass}_{testMethod}_{timestamp}
    /// </summary>
    public string? VhostName { get; private set; }

    /// <summary>
    /// The base RabbitMQ connection string (without vhost).
    /// Used internally for Management API calls.
    /// </summary>
    internal string? BaseConnectionString { get; private set; }

    /// <summary>
    /// The Management API port used for vhost operations.
    /// </summary>
    internal int? VhostManagementPort { get; private set; }

    /// <summary>
    /// Called by IntegrationTestBase to set up vhost before InitializeAsync.
    /// </summary>
    /// <param name="testClassName">Name of the test class</param>
    /// <param name="testMethodName">Name of the test method</param>
    /// <param name="baseConnectionString">Base RabbitMQ connection string (without vhost)</param>
    /// <param name="managementPort">RabbitMQ Management API port</param>
    internal async Task SetupVhostAsync(
        string testClassName,
        string testMethodName,
        string baseConnectionString,
        int? managementPort)
    {
        BaseConnectionString = baseConnectionString;
        VhostManagementPort = managementPort;

        // Generate unique vhost name with human-readable timestamp
        // Format: test_{class}_{method}_{timestamp}
        // Timestamp format: yyyy_MMM_dd_HH_mm_ss (e.g., 2025_Nov_24_15_05_33)
        var timestamp = DateTime.UtcNow.ToString("yyyy_MMM_dd_HH_mm_ss");
        var fullName = $"test_{testClassName}_{testMethodName}_{timestamp}";
        
        // Truncate to 63 characters (safe margin for RabbitMQ's 255 char limit)
        VhostName = fullName.Length > 63 ? fullName[..63] : fullName;

        // Create vhost via Management API
        using var rabbitMq = new RabbitMQ(baseConnectionString, managementPort);
        await rabbitMq.CreateVhostAsync(VhostName);

        // Extract username from connection string and set permissions
        var uri = new Uri(baseConnectionString);
        var username = uri.UserInfo.Split(':')[0];
        await rabbitMq.SetVhostPermissionsAsync(VhostName, username);
    }

    /// <summary>
    /// Called by IntegrationTestBase to clean up vhost after DisposeAsync.
    /// </summary>
    internal async Task TeardownVhostAsync()
    {
        if (string.IsNullOrEmpty(VhostName) || string.IsNullOrEmpty(BaseConnectionString))
            return;

        try
        {
            using var rabbitMq = new RabbitMQ(BaseConnectionString, VhostManagementPort);
            await rabbitMq.DeleteVhostAsync(VhostName);
        }
        catch
        {
            // Silently ignore cleanup errors
        }
    }
}
