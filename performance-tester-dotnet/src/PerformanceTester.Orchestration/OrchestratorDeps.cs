namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase-level delegates for the test orchestrator.
/// Each delegate has its internal plumbing (interfaces, config, CT) pre-bound
/// by <see cref="TestOrchestratorBuilder.Build"/>.
/// The orchestrator sequences these and threads inter-phase data.
/// </summary>
internal record OrchestratorDeps(
    RunSetup RunSetup,
    RunWarmup RunWarmup,
    RunEventTest RunEventTest,
    RunApiTest RunApiTest,
    RunReporting RunReporting,
    StopMonitoring StopMonitoring,
    DisconnectEventPublisher DisconnectEventPublisher);
