using System.Net.NetworkInformation;
using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;

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
    public static Task<Result<ProcessId, string>> FindServiceProcessIdAsync(
        Port port,
        TimeSpan timeout,
        SupportedPlatform platform,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Searching for service on port {Port} (timeout: {Timeout}s)",
            port.Value,
            timeout.TotalSeconds);

        return Poll.UntilSuccessOrTimeoutAsync(
                FindOnceAsync,
                interval: TimeSpan.FromSeconds(1),
                timeout: timeout,
                cancellationToken: cancellationToken)
            .Tap(processId => logger.LogInformation(
                "Found service on port {Port}: PID {ProcessId}",
                port.Value,
                processId))
            .TapError(_ => logger.LogWarning(
                "Service not found on port {Port} after {Timeout}s",
                port.Value,
                timeout.TotalSeconds))
            .MapError(_ => $"No service found on port {port} within {timeout}");

        // One attempt: guard that the port is listening, then ask the platform to find the process.
        // The OS dispatch lives on SupportedPlatform, so this stays platform-agnostic.
        Task<Result<ProcessId, string>> FindOnceAsync(CancellationToken token) =>
            new Result<Port, string>.Success(port)
                .Ensure(IsPortListening, p => $"Port {p} is not listening yet")
                .BindAsync(p => platform.FindProcessOnPortAsync(p, logger, token));
    }

    private static bool IsPortListening(Port port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = properties.GetActiveTcpListeners();
        return listeners.Any(l => l.Port == port.Value);
    }
}
