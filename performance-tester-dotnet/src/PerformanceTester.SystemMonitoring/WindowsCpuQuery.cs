using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Queries Windows host CPU usage via PowerShell.
/// Used in WSL2 or Windows to get actual Windows system CPU usage.
/// </summary>
internal static class WindowsCpuQuery
{
    private const int TimeoutSeconds = 10;

    /// <summary>
    /// Queries Windows host CPU usage via PowerShell Get-CimInstance.
    /// </summary>
    /// <returns>CPU percentage (0-100), or null if query fails.</returns>
    /// <remarks>
    /// Uses 'Get-CimInstance Win32_Processor | Select-Object -ExpandProperty LoadPercentage'
    /// which is fast (unlike Get-Counter which requires ~1 second sampling).
    /// Returns average CPU load across all processors.
    /// </remarks>
    public static async Task<double?> QueryAsync(
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        try
        {
            // Use PowerShell with Get-CimInstance (fast, unlike Get-Counter)
            // Output format: one number per processor, we average them
            var command = "Get-CimInstance Win32_Processor | Select-Object -ExpandProperty LoadPercentage";

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

            // Wait for completion with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                logger?.LogWarning("Windows CPU query (PowerShell) failed with exit code {ExitCode}: {Error}",
                    process.ExitCode, error);
                return null;
            }

            // Parse output: one number per line (one per processor)
            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var cpuValues = new List<double>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && double.TryParse(trimmed, out var cpuPercent))
                {
                    cpuValues.Add(cpuPercent);
                }
            }

            if (cpuValues.Count == 0)
            {
                logger?.LogWarning("Windows CPU query (PowerShell) returned no valid values: '{Output}'", output.Trim());
                return null;
            }

            // Average across all CPUs/sockets
            var avgCpu = cpuValues.Average();
            logger?.LogDebug("Windows CPU query succeeded: {CpuPercent:F1}%", avgCpu);
            return Math.Round(avgCpu, 2);
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("Windows CPU query (PowerShell) timed out after {Timeout}s", TimeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Windows CPU query (PowerShell) failed with exception");
            return null;
        }
    }
}
