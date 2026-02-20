using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Executes k6 binary as a process and parses JSON output in real-time.
/// Returns aggregated metrics when k6 process completes.
/// </summary>
internal sealed partial class K6Executor
{
    private const int K6AbortExitCode = 108;

    private readonly ILogger _logger;
    private readonly K6MetricsParser _metricsParser;
    private readonly string _k6Path;

    public K6Executor(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsParser = new K6MetricsParser();
        _k6Path = FindK6Binary();
    }

    /// <summary>
    /// Executes k6 with the specified script and returns execution result including metrics and abort info.
    /// </summary>
    /// <param name="scriptPath">Path to k6 script file.</param>
    /// <param name="totalDuration">Total expected test duration for progress reporting.</param>
    /// <param name="reportApiLoadProgress">Optional progress reporter for real-time updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>K6 execution result with metrics and abort information.</returns>
    /// <exception cref="InvalidOperationException">k6 binary not found or execution failed (non-abort errors).</exception>
    public async Task<K6ExecutionResult> ExecuteAsync(
        string scriptPath,
        TimeSpan totalDuration,
        ReportApiLoadProgressDelegate reportApiLoadProgress,
        CancellationToken cancellationToken)
    {
        ValidateK6Binary();

        var startInfo = new ProcessStartInfo
        {
            FileName = _k6Path,
            Arguments = $"run --out json=- \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stderrBuilder = new StringBuilder();
        var metrics = new List<K6Metric>();

        _logger.LogInformation("Starting k6: {Command}", $"k6 {startInfo.Arguments}");

        process.Start();
        var testStartTime = DateTime.UtcNow;

        // Read stderr asynchronously (error detection)
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
            {
                stderrBuilder.AppendLine(line);
                _logger.LogWarning("k6 stderr: {Line}", line);
            }
        }, cancellationToken);

        // Progress tracking state
        var lastProgressReport = DateTime.UtcNow;
        var requestCount = 0;
        var successCount = 0;
        var failedCount = 0;

        // Read and parse stdout (metrics)
        await foreach (var line in process.StandardOutput.ReadLinesAsync(cancellationToken))
        {
            _metricsParser.ParseLine(line).Match(
                success: s =>
                {
                    var metric = s.Value;
                    metrics.Add(metric);

                    // Update counts from metric
                    if (metric.Metric == "http_req_duration")
                    {
                        requestCount++;
                    }
                    else if (metric.Metric == "http_req_failed" && metric.Data?.Value > 0)
                    {
                        failedCount++;
                    }

                    // Report progress every 500ms (avoid flooding)
                    if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds < 500)
                    {
                        return;
                    }

                    var elapsed = (DateTime.UtcNow - testStartTime).TotalSeconds;
                    successCount = requestCount - failedCount;
                    reportApiLoadProgress(new ApiLoadProgress(
                        ElapsedSeconds: elapsed,
                        TotalSeconds: totalDuration.TotalSeconds,
                        RequestCount: requestCount,
                        SuccessCount: successCount,
                        FailedCount: failedCount));
                    lastProgressReport = DateTime.UtcNow;
                },
                failure: _ => { });
        }

        // Wait for process to exit and stderr reading to complete
        await process.WaitForExitAsync(cancellationToken);
        await stderrTask;

        var exitCode = process.ExitCode;
        var stderr = stderrBuilder.ToString();
        _logger.LogInformation("k6 exited with code: {ExitCode}", exitCode);

        // Handle abort (exit code 108) - return metrics with abort info
        if (exitCode == K6AbortExitCode)
        {
            var abortReason = ExtractAbortReason(stderr);
            _logger.LogWarning("k6 test was aborted: {AbortReason}", abortReason);
            return new K6ExecutionResult(metrics, WasAborted: true, AbortReason: abortReason);
        }

        // Check for other errors
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"k6 execution failed with exit code {exitCode}. Stderr:\n{stderr}");
        }

        return new K6ExecutionResult(metrics);
    }

    /// <summary>
    /// Extracts abort reason from k6 stderr output.
    /// </summary>
    private static string ExtractAbortReason(string stderr)
    {
        // k6 abort messages typically contain "test aborted:" or similar
        var match = AbortReasonRegex().Match(stderr);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try to find any line containing "Aborting" from our script
        var abortingMatch = AbortingMessageRegex().Match(stderr);
        if (abortingMatch.Success)
        {
            return abortingMatch.Groups[1].Value.Trim();
        }

        return "Test aborted (reason not found in output)";
    }

    [GeneratedRegex(@"test aborted:\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AbortReasonRegex();

    [GeneratedRegex(@"(Aborting:.+?)(?:\n|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AbortingMessageRegex();

    /// <summary>
    /// Finds k6 binary by checking multiple locations.
    /// Checks: PATH, ~/.local/bin/k6, /usr/local/bin/k6
    /// </summary>
    /// <returns>Path to k6 binary.</returns>
    /// <exception cref="InvalidOperationException">k6 binary not found.</exception>
    private string FindK6Binary()
    {
        // Locations to check (in order of preference)
        var candidatePaths = new[]
        {
            "k6",  // System PATH
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "k6"),
            "/usr/local/bin/k6"
        };

        foreach (var candidatePath in candidatePaths)
        {
            if (TryValidateK6Binary(candidatePath, out var validPath))
            {
                _logger.LogDebug("✓ k6 binary found at: {Path}", validPath);
                return validPath;
            }
        }

        throw new InvalidOperationException(
            "k6 binary not found in any of the following locations: " +
            string.Join(", ", candidatePaths) + ". " +
            "Install k6: https://k6.io/docs/getting-started/installation/");
    }

    /// <summary>
    /// Validates that k6 binary is available at the specified path.
    /// </summary>
    /// <exception cref="InvalidOperationException">k6 binary validation failed.</exception>
    private void ValidateK6Binary()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _k6Path,
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"Failed to start k6 at: {_k6Path}");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"k6 at {_k6Path} returned non-zero exit code for 'k6 version'");
            }

            _logger.LogDebug("✓ k6 binary validated at: {Path}", _k6Path);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"k6 binary validation failed for: {_k6Path}. Install k6: https://k6.io/docs/getting-started/installation/",
                ex);
        }
    }

    /// <summary>
    /// Tries to validate k6 binary at the specified path.
    /// </summary>
    /// <param name="path">Path to k6 binary.</param>
    /// <param name="validPath">Validated path if successful.</param>
    /// <returns>True if k6 binary is valid at the path, false otherwise.</returns>
    private bool TryValidateK6Binary(string path, out string validPath)
    {
        validPath = path;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
