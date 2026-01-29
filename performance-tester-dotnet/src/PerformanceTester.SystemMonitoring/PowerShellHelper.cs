using System.Diagnostics;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Helper to locate PowerShell executable across different environments.
/// </summary>
internal static class PowerShellHelper
{
    private static readonly Lazy<string?> _cachedPath = new(FindPowerShellPath);

    /// <summary>
    /// Gets whether PowerShell is available and functional in this environment.
    /// </summary>
    /// <remarks>
    /// Returns false in containerized environments without Windows filesystem access,
    /// native Linux systems, or when PowerShell validation fails.
    /// </remarks>
    public static bool IsAvailable => _cachedPath.Value != null;

    /// <summary>
    /// Gets the path to PowerShell executable.
    /// Checks multiple locations for WSL2 and Windows compatibility.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when PowerShell is not available in this environment.
    /// </exception>
    public static string GetPowerShellPath() =>
        _cachedPath.Value ?? throw new InvalidOperationException(
            "PowerShell is not available in this environment. " +
            "This typically occurs in containerized environments without Windows filesystem access.");

    private static string? FindPowerShellPath()
    {
        // PATH candidates - validate by execution (lets OS handle PATH resolution)
        string[] pathCandidates = ["powershell.exe", "pwsh"];

        foreach (var candidate in pathCandidates)
        {
            if (TryValidatePowerShell(candidate))
                return candidate;
        }

        // Explicit paths - validate by file existence first (faster), then execution
        string[] explicitPaths =
        [
            "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", // WSL2 standard
            "/mnt/c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe", // WSL2 32-bit
            "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", // Native Windows
        ];

        foreach (var path in explicitPaths)
        {
            if (File.Exists(path) && TryValidatePowerShell(path))
                return path;
        }

        // No PowerShell available - return null (caller should check IsAvailable first)
        return null;
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
