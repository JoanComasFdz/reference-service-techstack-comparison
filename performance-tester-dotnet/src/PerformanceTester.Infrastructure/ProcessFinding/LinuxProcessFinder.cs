using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Linux-specific process finder using lsof or ss command.
/// Tries lsof first, falls back to ss if lsof is not available.
/// </summary>
internal sealed partial class LinuxProcessFinder : IProcessFinder
{
    private readonly ILogger<LinuxProcessFinder> _logger;

    public LinuxProcessFinder(ILogger<LinuxProcessFinder> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int?> FindProcessOnPortAsync(int port, CancellationToken cancellationToken)
    {
        // Try lsof first (most reliable)
        var pid = await TryFindWithLsofAsync(port, cancellationToken);
        if (pid.HasValue)
        {
            return pid;
        }

        // Fallback to ss (available in most Linux distributions)
        return await TryFindWithSsAsync(port, cancellationToken);
    }

    private async Task<int?> TryFindWithLsofAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            // Use lsof command: lsof -ti :PORT
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lsof",
                    Arguments = $"-ti :{port}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var pidString = output.Trim().Split('\n').FirstOrDefault();
                if (int.TryParse(pidString, out var pid))
                {
                    return pid;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "lsof command failed for port {Port} (may not be installed)", port);
            return null;
        }
    }

    private async Task<int?> TryFindWithSsAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            // Use ss command: ss -tlnp sport = :PORT
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ss",
                    Arguments = $"-tlnp sport = :{port}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Parse ss output: users:(("processName",pid=12345,fd=3))
                // Example: LISTEN 0   1   0.0.0.0:8080   0.0.0.0:*   users:(("python3",pid=24940,fd=3))
                var match = PidRegex().Match(output);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
                {
                    return pid;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ss command failed for port {Port}", port);
            return null;
        }
    }

    [GeneratedRegex(@"pid=(\d+)", RegexOptions.Compiled)]
    private static partial Regex PidRegex();
}
