using JoanComasFdz.Result;
using PerformanceTester.Reporting;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Runs Setup phase: service discovery, infrastructure init, database/queue clearing.
/// testRunId is generated at runtime by the orchestrator.
/// </summary>
internal delegate Task<Result<int, string>> RunSetup(Guid testRunId);

/// <summary>
/// Runs Warmup phase: non-measured warmup events and API calls.
/// All parameters (config, logger, CT, internal delegates) are pre-bound.
/// </summary>
internal delegate Task<Result<Unit, string>> RunWarmup();

/// <summary>
/// Runs Event Test phase: concurrent publish/consume with monitoring.
/// serviceProcessId comes from Setup output. progress for internal reporting.
/// </summary>
internal delegate Task<Result<EventTestPhase.Output, string>> RunEventTest(int serviceProcessId, IProgress<PhaseInfo>? progress);

/// <summary>
/// Runs API Load Test phase: k6 load test execution.
/// progress for internal reporting.
/// </summary>
internal delegate Task<Result<ApiTestPhase.Output, string>> RunApiTest(IProgress<PhaseInfo>? progress);

/// <summary>
/// Runs Reporting phase: metrics collection, report and chart generation.
/// testResult is assembled at runtime from all phase outputs.
/// </summary>
internal delegate Task<Result<TestReport, string>> RunReporting(TestResult testResult);

/// <summary>
/// Stops all monitoring BackgroundServices (process, system, Docker).
/// Bound to IHost.StopAsync with CancellationToken.None for safe cleanup.
/// </summary>
internal delegate Task StopMonitoring();

/// <summary>
/// Disconnects the RabbitMQ event publisher connection.
/// Bound to IEventPublisher.DisconnectAsync with CancellationToken.None for safe cleanup.
/// </summary>
internal delegate Task DisconnectEventPublisher();
