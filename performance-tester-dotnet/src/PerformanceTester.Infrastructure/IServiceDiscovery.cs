using JoanComasFdz.Result;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Service for discovering processes listening on network ports.
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// Finds the process ID listening on the specified port.
    /// </summary>
    /// <param name="port">The port number to check (1-65535).</param>
    /// <param name="timeout">Maximum time to wait for service to appear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with process ID if found, or Failure with reason if not found within timeout.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Port is not in valid range.</exception>
    /// <exception cref="OperationCanceledException">Operation was cancelled.</exception>
    Task<Result<int>> FindServiceProcessIdAsync(int port, TimeSpan timeout, CancellationToken cancellationToken = default);
}
