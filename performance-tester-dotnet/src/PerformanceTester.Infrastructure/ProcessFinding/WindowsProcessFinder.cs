using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Windows-specific process finder using PowerShell Get-NetTCPConnection.
/// </summary>
internal sealed class WindowsProcessFinder : IProcessFinder
{
    private readonly ILogger<WindowsProcessFinder> _logger;

    public WindowsProcessFinder(ILogger<WindowsProcessFinder> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int?> FindProcessOnPortAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            // Use PowerShell: Get-NetTCPConnection -LocalPort {port} | Select-Object -ExpandProperty OwningProcess
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess\"",
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
                    return pid;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PowerShell command failed for port {Port}", port);
            return null;
        }
    }
}
