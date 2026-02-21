using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.Reporting.ValueObjects;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Configuration for a complete performance test run.
/// </summary>
/// <param name="EventCount">Number of events to publish and consume</param>
/// <param name="ApiDuration">Duration of API load test (parsed from CLI string like "30s")</param>
/// <param name="ApiWorkers">Number of concurrent API workers</param>
/// <param name="InactivityTimeout">Timeout for consumer inactivity, parsed from duration string (e.g., 120s, 2m)</param>
/// <param name="WarmupEventCount">Number of events for warmup phase (0-10,000)</param>
/// <param name="WarmupApiCallCount">Number of HTTP calls to make during API warmup (0-1,000)</param>
/// <param name="WarmupInactivityTimeout">Timeout for consumer inactivity during warmup, parsed from duration string (e.g., 30s, 1m)</param>
/// <param name="ServicePort">Port where service is running (1-65535)</param>
/// <param name="DatabaseName">PostgreSQL database name for the service (non-empty)</param>
/// <param name="ResultsFolder">Directory to save test results (may be created if it doesn't exist)</param>
/// <param name="RabbitMqContainerName">Name of RabbitMQ Docker container (non-empty)</param>
/// <param name="PostgresContainerName">Name of PostgreSQL Docker container (non-empty)</param>
/// <param name="MaxConsecutiveApiFailures">Maximum consecutive API failures before aborting load test (default: 3)</param>
public record TestConfiguration(
    EventCount EventCount,
    ApiDuration ApiDuration,
    WorkerCount ApiWorkers,
    Port ServicePort,
    WarmupEventsCount WarmupEventCount,
    WarmupApiCallsCount WarmupApiCallCount,
    DatabaseName DatabaseName,
    ResultsOutputFolder ResultsFolder,
    RabbitMqContainerName RabbitMqContainerName,
    PostgresContainerName PostgresContainerName,
    InactivityTimeout InactivityTimeout,
    InactivityTimeout WarmupInactivityTimeout,
    MaxConsecutiveFailures MaxConsecutiveApiFailures)
{
    /// <summary>
    /// Gets the API URL based on the service port.
    /// </summary>
    public ServiceUrl ApiUrl => ServiceUrl.FromString($"http://localhost:{ServicePort.Value}/kpi");
}
