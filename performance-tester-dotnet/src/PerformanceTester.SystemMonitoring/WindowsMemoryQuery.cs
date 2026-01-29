using System.Diagnostics;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Queries Windows host memory usage via PowerShell.
/// Used in WSL2 to get accurate Windows host memory instead of WSL's limited view.
/// Port of Python's _get_windows_memory_usage() function.
/// </summary>
internal static class WindowsMemoryQuery
{
    private const int TimeoutSeconds = 5;

    /// <summary>
    /// Queries Windows host memory via PowerShell.
    /// </summary>
    /// <returns>Memory info (total, used, percent) in MB, or null if query fails.</returns>
    /// <remarks>
    /// PowerShell command (matches Python exactly):
    /// $os = Get-CimInstance Win32_OperatingSystem;
    /// Write-Output "$($os.TotalVisibleMemorySize),$($os.FreePhysicalMemory)"
    ///
    /// Returns values in KB, which we convert to MB.
    /// </remarks>
    public static async Task<WindowsMemoryInfo?> QueryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var command =
                "$os = Get-CimInstance Win32_OperatingSystem; " +
                "Write-Output \"$($os.TotalVisibleMemorySize),$($os.FreePhysicalMemory)\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
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

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                return null;
            }

            // Parse output: "TotalKB,FreeKB"
            var parts = output.Trim().Split(',');
            if (parts.Length != 2)
            {
                return null;
            }

            if (!long.TryParse(parts[0], out var totalKb) ||
                !long.TryParse(parts[1], out var freeKb))
            {
                return null;
            }

            var totalMb = totalKb / 1024.0;
            var freeMb = freeKb / 1024.0;
            var usedMb = totalMb - freeMb;
            var percent = (totalMb > 0) ? (usedMb / totalMb) * 100.0 : 0;

            return new WindowsMemoryInfo(
                TotalMb: Math.Round(totalMb, 2),
                UsedMb: Math.Round(usedMb, 2),
                Percent: Math.Round(percent, 2));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Windows host memory information.
    /// </summary>
    /// <param name="TotalMb">Total physical memory in MB.</param>
    /// <param name="UsedMb">Used memory in MB.</param>
    /// <param name="Percent">Usage percentage (0-100).</param>
    public sealed record WindowsMemoryInfo(double TotalMb, double UsedMb, double Percent);
}
