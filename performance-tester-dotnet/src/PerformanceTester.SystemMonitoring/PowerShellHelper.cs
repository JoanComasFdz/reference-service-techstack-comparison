using System.Diagnostics;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Helper to locate PowerShell executable across different environments.
/// </summary>
internal static class PowerShellHelper
{
    private static readonly Lazy<string> _cachedPath = new(FindPowerShellPath);

    /// <summary>
    /// Gets the path to PowerShell executable.
    /// Checks multiple locations for WSL2 and Windows compatibility.
    /// </summary>
    public static string GetPowerShellPath() => _cachedPath.Value;

    private static string FindPowerShellPath()
    {
        // PATH candidates - validate by execution (lets OS handle PATH resolution)
        string[] pathCandidates = ["powershell.exe", "pwsh"];

        foreach (var candidate in pathCandidates)
        {
            if (TryValidatePowerShell(candidate))
                return candidate;
        }

        // Explicit paths - validate by file existence (faster for known locations)
        string[] explicitPaths =
        [
            "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", // WSL2 standard
            "/mnt/c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe", // WSL2 32-bit
            "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", // Native Windows
        ];

        foreach (var path in explicitPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Fallback - let OS try to resolve
        return "powershell.exe";
    }

    /// <summary>
    /// Validates PowerShell by attempting to run a simple command.
    /// This lets the OS handle proper PATH resolution.
    /// </summary>
    private static bool TryValidatePowerShell(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-NoProfile -Command \"exit 0\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return false;

            var completed = process.WaitForExit(5000);
            if (!completed)
            {
                try { process.Kill(); } catch { /* Ignore kill errors */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
