namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Helper to locate PowerShell executable across different environments.
/// </summary>
internal static class PowerShellHelper
{
    private static string? _cachedPath;

    /// <summary>
    /// Gets the path to PowerShell executable.
    /// Checks multiple locations for WSL2 and Windows compatibility.
    /// </summary>
    public static string GetPowerShellPath()
    {
        if (_cachedPath != null)
            return _cachedPath;

        // Possible PowerShell locations
        var candidates = new[]
        {
            "powershell.exe", // If in PATH
            "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", // WSL2 standard
            "/mnt/c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe", // WSL2 32-bit
            "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", // Native Windows
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _cachedPath = candidate;
                return candidate;
            }
        }

        // Default fallback - let the OS try to find it
        _cachedPath = "powershell.exe";
        return _cachedPath;
    }
}
