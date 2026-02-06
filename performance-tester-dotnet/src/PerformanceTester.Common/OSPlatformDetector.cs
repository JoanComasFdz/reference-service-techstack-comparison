using System;
using System.Runtime.InteropServices;

namespace PerformanceTester.Common;

/// <summary>
/// Detects the current operating system platform.
/// Uses RuntimeInformation to detect Windows or Linux.
/// </summary>
public static class OSPlatformDetector
{
    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    /// <returns>The detected platform (Windows or Linux).</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the current platform is not Windows or Linux.
    /// </exception>
    public static SupportedPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return SupportedPlatform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return SupportedPlatform.Windows;
        }

        throw new PlatformNotSupportedException(
            $"Platform {RuntimeInformation.OSDescription} is not supported. Only Linux and Windows are supported.");
    }
}
