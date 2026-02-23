using System.Net.NetworkInformation;
using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Infrastructure.ValueObjects.ProcessId, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Service discovery logic for finding processes listening on network ports.
/// Pure static class with explicit parameters (Guidelines 1, 2).
/// Accepts <see cref="Port"/> value object — port range is guaranteed valid (Guideline 04-05).
/// </summary>
internal static class ServiceDiscovery
{
    /// <summary>
    /// Polls for a process listening on the specified port until found or timeout.
    /// </summary>
    public static async Task<Result<ProcessId, string>> FindServiceProcessIdAsync(
        Port port,
        TimeSpan timeout,
        FindProcessOnPortDelegate findProcessOnPort,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Searching for service on port {Port} (timeout: {Timeout}s)",
            port.Value,
            timeout.TotalSeconds);

        var startTime = DateTime.UtcNow;
        var lastLogTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if port is listening before attempting to find process
            if (IsPortListening(port))
            {
                var result = await findProcessOnPort(port, cancellationToken);
                if (result.IsSuccess)
                {
                    logger.LogInformation(
                        "Found service on port {Port}: PID {ProcessId}",
                        port.Value,
                        result.SuccessValue);
                    return result;
                }
            }

            // Log progress every 5 seconds
            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 5)
            {
                var elapsed = DateTime.UtcNow - startTime;
                logger.LogDebug(
                    "Still searching for service on port {Port} (elapsed: {Elapsed}s)",
                    port.Value,
                    (int)elapsed.TotalSeconds);
                lastLogTime = DateTime.UtcNow;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        logger.LogWarning(
            "Service not found on port {Port} after {Timeout}s",
            port.Value,
            timeout.TotalSeconds);
        return new Failure($"No service found on port {port} within {timeout}");
    }

    private static bool IsPortListening(Port port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = properties.GetActiveTcpListeners();
        return listeners.Any(l => l.Port == port.Value);
    }
}
