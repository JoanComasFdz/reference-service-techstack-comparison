using System.Text.RegularExpressions;

namespace PerformanceTester.Reporting.ReportGeneration.Utilities;

/// <summary>
/// Utility for sanitizing process names for use in filenames.
/// Implements 5-step algorithm matching Python implementation.
/// </summary>
internal static class ProcessNameSanitizer
{
    /// <summary>
    /// Sanitizes a process name for use in filenames.
    /// </summary>
    /// <param name="processName">The process name to sanitize.</param>
    /// <returns>Sanitized process name safe for use in filenames.</returns>
    /// <remarks>
    /// 5-Step Algorithm:
    /// 1. Remove file extensions (.jar, .dll, .exe)
    /// 2. Replace spaces and special characters with dashes
    /// 3. Remove consecutive dashes
    /// 4. Remove leading/trailing dashes
    /// 5. Convert to lowercase
    ///
    /// Examples:
    /// - "java21SpringBootGraalReferenceService" → "java21springbootgraalreferenceservice"
    /// - "dotnet9AotReferenceService.exe" → "dotnet9aotreferenceservice"
    /// - "python ReferenceService" → "pythonreferenceservice"
    /// - "go-reference-service.dll" → "go-reference-service"
    /// - "rust__service--test.jar" → "rust-service-test"
    /// </remarks>
    public static string Sanitize(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var sanitized = processName;

        // Step 1: Remove file extensions (.jar, .dll, .exe)
        sanitized = Regex.Replace(sanitized, @"\.(jar|dll|exe)$", "", RegexOptions.IgnoreCase);

        // Step 2: Replace spaces and special characters with dashes
        // Keep only alphanumeric characters, dots, and dashes
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9.-]+", "-");

        // Step 3: Remove consecutive dashes
        sanitized = Regex.Replace(sanitized, @"-+", "-");

        // Step 4: Remove leading/trailing dashes
        sanitized = sanitized.Trim('-');

        // Step 5: Convert to lowercase
        sanitized = sanitized.ToLowerInvariant();

        return sanitized;
    }
}
