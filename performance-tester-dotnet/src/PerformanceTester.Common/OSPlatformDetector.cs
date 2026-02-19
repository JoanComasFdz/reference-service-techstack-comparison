using System.Runtime.InteropServices;
using PerformanceTester.Functional;
using static PerformanceTester.Functional.Result<PerformanceTester.Common.SupportedPlatform, string>;

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
    /// <returns>Success with the detected platform (Windows or Linux), or Failure if the platform is not supported.</returns>
    public static Result<SupportedPlatform, string> GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new Success(new SupportedPlatform.Linux());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Success(new SupportedPlatform.Windows());
        }

        return new Failure(
            $"Platform {RuntimeInformation.OSDescription} is not supported. Only Linux and Windows are supported.");
    }
}
