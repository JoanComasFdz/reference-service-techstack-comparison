using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Platform-specific process finder for discovering PIDs on ports.
/// Named delegate replacing IProcessFinder interface (Guideline 12).
/// Returns Success with ProcessId if found, Failure with reason otherwise (Guideline 15).
/// </summary>
internal delegate Task<Result<ProcessId, string>> FindProcessOnPortDelegate(Port port, CancellationToken cancellationToken);
