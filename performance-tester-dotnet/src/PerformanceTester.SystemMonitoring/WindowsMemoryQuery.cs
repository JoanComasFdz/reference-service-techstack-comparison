using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Queries Windows host memory usage via PowerShell.
/// Used in WSL2 to get accurate Windows host memory instead of WSL's limited view.
/// Port of Python's _get_windows_memory_usage() function.
/// </summary>
internal static class WindowsMemoryQuery
{
    private const int TimeoutSeconds = 15; // Generous timeout for slow WMI queries
    private const string Command =
        "$os = Get-CimInstance Win32_OperatingSystem; " +
        "Write-Output \"$($os.TotalVisibleMemorySize),$($os.FreePhysicalMemory)\"";

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
    public static async Task<WindowsMemoryInfo?> QueryAsync(
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        var result = await PowerShellExecutor.ExecuteAsync(
            Command, TimeoutSeconds, cancellationToken, logger);

        if (result is null)
        {
            return null;
        }

        if (!result.Success)
        {
            logger?.LogWarning("Windows memory query failed with exit code {ExitCode}: {Error}",
                result.ExitCode, result.Error);
            return null;
        }

        return ParseMemoryOutput(result.Output, logger);
    }

    private static WindowsMemoryInfo? ParseMemoryOutput(string output, ILogger? logger)
    {
        var parts = output.Trim().Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            logger?.LogWarning("Windows memory query returned unexpected output (expected 2 parts, got {Count}): {Output}",
                parts.Length, output);
            return null;
        }

        if (!long.TryParse(parts[0].Trim(), out var totalKb) ||
            !long.TryParse(parts[1].Trim(), out var freeKb))
        {
            logger?.LogWarning("Windows memory query returned non-numeric values: {Output}", output);
            return null;
        }

        var totalMb = totalKb / 1024.0;
        var freeMb = freeKb / 1024.0;
        var usedMb = totalMb - freeMb;
        var percent = (totalMb > 0) ? (usedMb / totalMb) * 100.0 : 0;

        logger?.LogDebug("Windows memory query succeeded: {UsedMb:F1} MB / {TotalMb:F1} MB ({Percent:F1}%)",
            usedMb, totalMb, percent);

        return new WindowsMemoryInfo(
            TotalMb: Math.Round(totalMb, 2),
            UsedMb: Math.Round(usedMb, 2),
            Percent: Math.Round(percent, 2));
    }

    /// <summary>
    /// Windows host memory information.
    /// </summary>
    /// <param name="TotalMb">Total physical memory in MB.</param>
    /// <param name="UsedMb">Used memory in MB.</param>
    /// <param name="Percent">Usage percentage (0-100).</param>
    public sealed record WindowsMemoryInfo(double TotalMb, double UsedMb, double Percent);
}
