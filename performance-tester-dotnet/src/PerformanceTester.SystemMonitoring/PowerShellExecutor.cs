using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Executes PowerShell commands with timeout and error handling.
/// </summary>
internal static class PowerShellExecutor
{
    /// <summary>
    /// Result of a PowerShell command execution.
    /// </summary>
    /// <param name="Success">Whether the command executed with exit code 0.</param>
    /// <param name="Output">Standard output from the command.</param>
    /// <param name="Error">Standard error from the command.</param>
    /// <param name="ExitCode">The process exit code.</param>
    public sealed record ExecutionResult(
        bool Success,
        string Output,
        string Error,
        int ExitCode);

    /// <summary>
    /// Executes a PowerShell command with timeout.
    /// </summary>
    /// <param name="command">The PowerShell command to execute.</param>
    /// <param name="timeoutSeconds">Timeout in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>Execution result, or null if execution failed (timeout/exception).</returns>
    public static async Task<ExecutionResult?> ExecuteAsync(
        string command,
        int timeoutSeconds,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = PowerShellHelper.GetPowerShellPath(),
                    Arguments = $"-NoProfile -Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            return new ExecutionResult(
                Success: process.ExitCode == 0,
                Output: output,
                Error: error,
                ExitCode: process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("PowerShell command timed out after {Timeout}s", timeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "PowerShell command failed with exception");
            return null;
        }
    }
}
