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
/// Runs Teardown phase: disconnect event publisher, stop monitoring services.
/// Pre-bound with the active CancellationToken — cancellable during normal flow.
/// Must complete before reporting can collect metrics.
/// </summary>
internal delegate Task<Result<Unit, string>> RunTeardown();

/// <summary>
/// Best-effort resource cleanup for the finally block.
/// Pre-bound with CancellationToken.None — must complete even after cancellation or failure.
/// Runs the same operations as <see cref="RunTeardown"/> but is not cancellable.
/// </summary>
internal delegate Task CleanupResources();

/// <summary>
/// Runs Reporting phase: metrics collection, report and chart generation.
/// testResult is assembled at runtime from all phase outputs.
/// </summary>
internal delegate Task<Result<TestReport, string>> RunReporting(TestResult testResult);
