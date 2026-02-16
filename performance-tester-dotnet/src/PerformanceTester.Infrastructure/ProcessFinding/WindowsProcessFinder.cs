using System.Diagnostics;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static JoanComasFdz.Result.Result<int, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Windows-specific process finder using PowerShell Get-NetTCPConnection.
/// </summary>
internal static class WindowsProcessFinder
{
    /// <summary>
    /// Finds the process ID listening on the specified port using PowerShell.
    /// </summary>
    public static async Task<Result<int, string>> FindProcessOnPortAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use PowerShell: Get-NetTCPConnection -LocalPort {port} | Select-Object -ExpandProperty OwningProcess
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Get-NetTCPConnection -LocalPort {port.Value} -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess\"",
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
                if (int.TryParse(output.Trim(), out var pid))
                {
                    return new Success(pid);
                }
            }

            return new Failure($"No process found listening on port {port}");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "PowerShell command failed for port {Port}", port.Value);
            return new Failure($"PowerShell command failed for port {port}: {ex.Message}");
        }
    }
}
