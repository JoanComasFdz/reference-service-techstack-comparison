using System.Net.NetworkInformation;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using static JoanComasFdz.Result.Result<int, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Service for discovering processes listening on network ports.
/// Uses injected IProcessFinder for platform-specific process discovery.
/// </summary>
internal sealed class ServiceDiscovery : IServiceDiscovery
{
    private readonly IProcessFinder _processFinder;
    private readonly ILogger<ServiceDiscovery> _logger;

    public ServiceDiscovery(IProcessFinder processFinder, ILogger<ServiceDiscovery> logger)
    {
        _processFinder = processFinder;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<int, string>> FindServiceProcessIdAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535");
        }

        _logger.LogInformation("Searching for service on port {Port} (timeout: {Timeout}s)", port, timeout.TotalSeconds);

        var startTime = DateTime.UtcNow;
        var lastLogTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if port is listening before attempting to find process
            if (IsPortListening(port))
            {
                var processId = await _processFinder.FindProcessOnPortAsync(port, cancellationToken);
                if (processId.HasValue)
                {
                    _logger.LogInformation("✓ Found service on port {Port}: PID {ProcessId}", port, processId.Value);
                    return new Success(processId.Value);
                }
            }

            // Log progress every 5 seconds
            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 5)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogDebug("Still searching for service on port {Port} (elapsed: {Elapsed}s)", port, (int)elapsed.TotalSeconds);
                lastLogTime = DateTime.UtcNow;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        _logger.LogWarning("⚠️ Service not found on port {Port} after {Timeout}s", port, timeout.TotalSeconds);
        return new Failure($"No service found on port {port} within {timeout}");
    }

    private bool IsPortListening(int port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = properties.GetActiveTcpListeners();
        return listeners.Any(l => l.Port == port);
    }
}
