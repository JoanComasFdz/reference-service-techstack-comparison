using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Queries Windows host CPU usage via PowerShell.
/// Used in WSL2 or Windows to get actual Windows system CPU usage.
/// </summary>
internal static class WindowsCpuQuery
{
    private const int TimeoutSeconds = 10;
    private const string Command = "Get-CimInstance Win32_Processor | Select-Object -ExpandProperty LoadPercentage";

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
        var result = await PowerShellExecutor.ExecuteAsync(
            Command, TimeoutSeconds, cancellationToken, logger);

        if (result is null)
            return null;

        if (!result.Success)
        {
            logger?.LogWarning("Windows CPU query failed with exit code {ExitCode}: {Error}",
                result.ExitCode, result.Error);
            return null;
        }

        return ParseCpuOutput(result.Output, logger);
    }

    private static double? ParseCpuOutput(string output, ILogger? logger)
    {
        var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var cpuValues = new List<double>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) && double.TryParse(trimmed, out var cpuPercent))
                cpuValues.Add(cpuPercent);
        }

        if (cpuValues.Count == 0)
        {
            logger?.LogWarning("Windows CPU query returned no valid values: '{Output}'", output.Trim());
            return null;
        }

        var avgCpu = cpuValues.Average();
        logger?.LogDebug("Windows CPU query succeeded: {CpuPercent:F1}%", avgCpu);
        return Math.Round(avgCpu, 2);
    }
}
