using System;
using System.Runtime.InteropServices;

namespace PerformanceTester.Common;

/// <summary>
/// Default implementation of OS platform detection.
/// Uses RuntimeInformation to detect Windows or Linux.
/// </summary>
public sealed class OSPlatformDetector : IOSPlatformDetector
{
    /// <inheritdoc />
    public SupportedPlatform GetCurrentPlatform()
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
