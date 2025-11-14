namespace PerformanceTester.Reporting;

/// <summary>
/// Detects hardware and operating system information.
/// Matches Python system_info.py functionality.
/// </summary>
public interface ISystemInfoDetector
{
    /// <summary>
    /// Detects complete system information (CPU, RAM, disks, OS).
    /// Result is cached for performance (expensive operation).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>System information, or null if detection fails.</returns>
    Task<SystemInfo?> GetSystemInfoAsync(CancellationToken cancellationToken = default);
}
