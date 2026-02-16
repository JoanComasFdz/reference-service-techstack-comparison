using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Finds the process ID listening on the specified port.
/// Named delegate replacing IServiceDiscovery interface (Guideline 12).
/// </summary>
/// <param name="port">The validated port to check.</param>
/// <param name="timeout">Maximum time to wait for service to appear.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>
/// Success with process ID if found, or Failure with reason if not found within timeout.
/// </returns>
public delegate Task<Result<int, string>> FindServiceProcessId(
    Port port,
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
