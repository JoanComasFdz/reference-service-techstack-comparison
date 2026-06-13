using Microsoft.Extensions.Logging;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Platform-specific process finding as a named operation on the <see cref="SupportedPlatform"/> union.
/// The OS dispatch (the <c>Match</c>) lives here — on the type that owns the cases — so callers ask the
/// platform to find a process instead of branching on the OS themselves.
/// </summary>
internal static class PlatformProcessFinder
{
    /// <summary>
    /// Finds the process listening on <paramref name="port"/> using the finder appropriate to this platform.
    /// </summary>
    public static Task<Result<ProcessId, string>> FindProcessOnPortAsync(
        this SupportedPlatform platform,
        Port port,
        ILogger logger,
        CancellationToken cancellationToken) =>
        platform.Match(
            linux: _ => LinuxProcessFinder.FindProcessOnPortAsync(port, logger, cancellationToken),
            windows: _ => WindowsProcessFinder.FindProcessOnPortAsync(port, logger, cancellationToken));
}
