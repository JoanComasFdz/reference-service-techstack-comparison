namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Generates k6 test scripts for API load testing.
/// Uses template pattern with placeholder replacement.
/// </summary>
internal static class K6ScriptGenerator
{
    private const string ScriptTemplate = @"
import http from 'k6/http';
import { check } from 'k6';

export let options = {
  duration: '{{DURATION}}',
  vus: {{VUS}},
};

export default function() {
  let response = http.get('{{URL}}');
  check(response, {
    'status is 200': (r) => r.status === 200,
  });
}
";

    /// <summary>
    /// Generates a k6 script for the specified test parameters.
    /// </summary>
    /// <param name="targetUrl">Target endpoint URL.</param>
    /// <param name="duration">Test duration (formatted as k6 duration string, e.g., "30s", "2m").</param>
    /// <param name="virtualUsers">Number of virtual users.</param>
    /// <returns>k6 script content as string.</returns>
    public static string GenerateScript(string targetUrl, string duration, int virtualUsers) =>
        ScriptTemplate
            .Replace("{{URL}}", targetUrl)
            .Replace("{{DURATION}}", duration)
            .Replace("{{VUS}}", virtualUsers.ToString());

    /// <summary>
    /// Formats TimeSpan as k6 duration string (e.g., "30s", "2m", "1h").
    /// </summary>
    /// <param name="duration">Duration to format.</param>
    /// <returns>k6-compatible duration string.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h";
        }
        else if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }
        else
        {
            return $"{(int)duration.TotalSeconds}s";
        }
    }
}
