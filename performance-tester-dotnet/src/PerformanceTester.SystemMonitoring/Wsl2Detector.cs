namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Detects if running in WSL2 environment.
/// Port of Python's _is_wsl2() function from system_monitor.py.
/// </summary>
internal static class Wsl2Detector
{
    private const string ProcVersionPath = "/proc/version";

    /// <summary>
    /// Detects if the current environment is WSL2.
    /// </summary>
    /// <returns>True if running in WSL2, false otherwise.</returns>
    /// <remarks>
    /// Detection logic (matches Python exactly):
    /// 1. Check if /proc/version exists
    /// 2. Read contents and check for "microsoft" or "wsl" (case-insensitive)
    /// </remarks>
    public static bool IsWsl2()
    {
        try
        {
            if (!File.Exists(ProcVersionPath))
            {
                return false;
            }

            var versionInfo = File.ReadAllText(ProcVersionPath).ToLowerInvariant();
            return versionInfo.Contains("microsoft") || versionInfo.Contains("wsl");
        }
        catch
        {
            // Any error reading /proc/version means we're not in a Linux/WSL2 environment
            return false;
        }
    }
}
