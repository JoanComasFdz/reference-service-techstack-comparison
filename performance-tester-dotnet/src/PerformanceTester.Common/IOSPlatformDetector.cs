namespace PerformanceTester.Common;

/// <summary>
/// Detects the current operating system platform.
/// </summary>
public interface IOSPlatformDetector
{
    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    /// <returns>The detected platform (Windows or Linux).</returns>
    /// <exception cref="System.PlatformNotSupportedException">
    /// Thrown when the current platform is not Windows or Linux.
    /// </exception>
    SupportedPlatform GetCurrentPlatform();
}
