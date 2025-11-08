namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Platform-specific process finder for discovering PIDs on ports.
/// Strategy interface for cross-platform port scanning.
/// </summary>
internal interface IProcessFinder
{
    /// <summary>
    /// Finds the process ID listening on the specified port.
    /// </summary>
    /// <param name="port">The port number to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process ID if found, null otherwise.</returns>
    Task<int?> FindProcessOnPortAsync(int port, CancellationToken cancellationToken);
}
