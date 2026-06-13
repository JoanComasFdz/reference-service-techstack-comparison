using System.Diagnostics;
using System.Text.RegularExpressions;
using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Infrastructure.ValueObjects.ProcessId, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Linux-specific process finder using lsof or ss command.
/// Tries lsof first, falls back to ss if lsof is not available.
/// </summary>
internal static partial class LinuxProcessFinder
{
    /// <summary>
    /// Finds the process ID listening on the specified port using Linux tools.
    /// </summary>
    public static async Task<Result<ProcessId, string>> FindProcessOnPortAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Try lsof first (most reliable)
        var pid = await TryFindWithLsofAsync(port, logger, cancellationToken);
        if (pid.HasValue)
        {
            return new Success(ProcessId.FromInt(pid.Value));
        }

        // Fallback to ss (available in most Linux distributions)
        pid = await TryFindWithSsAsync(port, logger, cancellationToken);
        if (pid.HasValue)
        {
            return new Success(ProcessId.FromInt(pid.Value));
        }

        return new Failure($"No process found listening on port {port}");
    }

    private static async Task<int?> TryFindWithLsofAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use lsof command: lsof -ti :PORT
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lsof",
                    Arguments = $"-ti :{port.Value}",
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
                return ParsePidFromLsofOutput(output);
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "lsof command failed for port {Port} (may not be installed)", port.Value);
            return null;
        }
    }

    private static async Task<int?> TryFindWithSsAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use ss command: ss -tlnp sport = :PORT
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ss",
                    Arguments = $"-tlnp sport = :{port.Value}",
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
                return ParsePidFromSsOutput(output);
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ss command failed for port {Port}", port.Value);
            return null;
        }
    }

    // Pure: parse lsof's PID-per-line output. Private — only the impure finder calls it.
    private static int? ParsePidFromLsofOutput(string output)
    {
        var pidString = output.Trim().Split('\n').FirstOrDefault();
        return int.TryParse(pidString, out var pid) ? pid : null;
    }

    // Pure: extract pid=NNN from ss output. Private — only the impure finder calls it.
    private static int? ParsePidFromSsOutput(string output)
    {
        var match = PidRegex().Match(output);
        return match.Success && int.TryParse(match.Groups[1].Value, out var pid) ? pid : null;
    }

    [GeneratedRegex(@"pid=(\d+)", RegexOptions.Compiled)]
    private static partial Regex PidRegex();
}
