namespace PerformanceTester.Orchestration;

/// <summary>
/// Represents a managed (non-exceptional) failure of a test run,
/// identifying which phase failed and why.
/// </summary>
public sealed record TestRunFailure(TestPhase Phase, string Message);
