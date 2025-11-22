namespace PerformanceTester.Orchestration;

/// <summary>
/// Configuration for a complete performance test run.
/// </summary>
/// <param name="EventCount">Number of events to publish and consume (default: 10000)</param>
/// <param name="ApiDuration">Duration of API load test (default: 30 seconds)</param>
/// <param name="ApiWorkers">Number of concurrent API workers (default: 1)</param>
/// <param name="InactivityTimeout">Timeout for consumer inactivity (default: 120 seconds)</param>
/// <param name="WarmupEventCount">Number of events for warmup phase (default: 200)</param>
/// <param name="WarmupApiDuration">Duration of API load test during warmup (default: 5 seconds)</param>
/// <param name="WarmupInactivityTimeout">Timeout for consumer inactivity during warmup (default: 30 seconds)</param>
/// <param name="ServicePort">Port where service is running (default: 8080)</param>
/// <param name="DatabaseName">PostgreSQL database name for the service</param>
/// <param name="ResultsFolder">Directory to save test results (default: ./test-results)</param>
/// <param name="RabbitMqContainerName">Name of RabbitMQ Docker container (default: performancetest-rabbitmq)</param>
/// <param name="PostgresContainerName">Name of PostgreSQL Docker container (default: performancetest-postgres)</param>
public record TestConfiguration(
    int EventCount = 10000,
    TimeSpan? ApiDuration = null,
    int ApiWorkers = 1,
    TimeSpan? InactivityTimeout = null,
    int WarmupEventCount = 200,
    TimeSpan? WarmupApiDuration = null,
    TimeSpan? WarmupInactivityTimeout = null,
    int ServicePort = 8080,
    string DatabaseName = "defaultdb",
    string ResultsFolder = "./test-results",
    string RabbitMqContainerName = "performancetest-rabbitmq",
    string PostgresContainerName = "performancetest-postgres")
{
    /// <summary>
    /// Gets the API duration with default value if not specified.
    /// </summary>
    public TimeSpan ApiDurationOrDefault => ApiDuration ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the inactivity timeout with default value if not specified.
    /// </summary>
    public TimeSpan InactivityTimeoutOrDefault => InactivityTimeout ?? TimeSpan.FromSeconds(120);

    /// <summary>
    /// Gets the warmup API duration with default value if not specified.
    /// </summary>
    public TimeSpan WarmupApiDurationOrDefault => WarmupApiDuration ?? TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the warmup inactivity timeout with default value if not specified.
    /// </summary>
    public TimeSpan WarmupInactivityTimeoutOrDefault => WarmupInactivityTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the API URL based on the service port.
    /// </summary>
    public string ApiUrl => $"http://localhost:{ServicePort}/kpi";
}
