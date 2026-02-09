using PerformanceTester.Orchestration.ValueObjects;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Configuration for a complete performance test run.
/// </summary>
/// <param name="EventCount">Number of events to publish and consume</param>
/// <param name="ApiDuration">Duration of API load test (parsed from CLI string like "30s")</param>
/// <param name="ApiWorkers">Number of concurrent API workers</param>
/// <param name="InactivityTimeout">Timeout for consumer inactivity (default: 120 seconds)</param>
/// <param name="WarmupEventCount">Number of events for warmup phase (default: 200)</param>
/// <param name="WarmupApiCallCount">Number of HTTP calls to make during API warmup (default: 10)</param>
/// <param name="WarmupInactivityTimeout">Timeout for consumer inactivity during warmup (default: 30 seconds)</param>
/// <param name="ServicePort">Port where service is running (1-65535)</param>
/// <param name="DatabaseName">PostgreSQL database name for the service</param>
/// <param name="ResultsFolder">Directory to save test results (default: ./test-results)</param>
/// <param name="RabbitMqContainerName">Name of RabbitMQ Docker container (default: performancetest-rabbitmq)</param>
/// <param name="PostgresContainerName">Name of PostgreSQL Docker container (default: performancetest-postgres)</param>
/// <param name="MaxConsecutiveApiFailures">Maximum consecutive API failures before aborting load test (default: 3)</param>
public record TestConfiguration(
    EventCount EventCount,
    ApiDuration ApiDuration,
    WorkerCount ApiWorkers,
    Port ServicePort,
    TimeSpan? InactivityTimeout = null,
    int WarmupEventCount = 200,
    uint WarmupApiCallCount = 10,
    TimeSpan? WarmupInactivityTimeout = null,
    string DatabaseName = "defaultdb",
    string ResultsFolder = "./test-results",
    string RabbitMqContainerName = "performancetest-rabbitmq",
    string PostgresContainerName = "performancetest-postgres",
    int MaxConsecutiveApiFailures = 3)
{
    /// <summary>
    /// Gets the inactivity timeout with default value if not specified.
    /// </summary>
    public TimeSpan InactivityTimeoutOrDefault => InactivityTimeout ?? TimeSpan.FromSeconds(120);

    /// <summary>
    /// Gets the warmup inactivity timeout with default value if not specified.
    /// </summary>
    public TimeSpan WarmupInactivityTimeoutOrDefault => WarmupInactivityTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the API URL based on the service port.
    /// </summary>
    public string ApiUrl => $"http://localhost:{ServicePort.Value}/kpi";
}
