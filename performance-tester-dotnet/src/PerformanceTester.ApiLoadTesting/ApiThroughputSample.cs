namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Represents an API throughput sample at a specific point in time.
/// This model is owned by the ApiLoadTesting slice (producer-owned contract).
/// </summary>
/// <param name="Timestamp">When this sample was captured (UTC).</param>
/// <param name="RequestsPerSecond">API requests per second at this sample time.</param>
/// <param name="CumulativeRequestCount">Total requests processed since test started.</param>
/// <param name="ActiveVirtualUsers">Number of active virtual users (VUs) at this sample time.</param>
public record ApiThroughputSample(
    DateTimeOffset Timestamp,
    double RequestsPerSecond,
    int CumulativeRequestCount,
    int ActiveVirtualUsers);
