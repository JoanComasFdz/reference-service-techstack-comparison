using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Executes k6 binary as a process and parses JSON output in real-time.
/// Returns aggregated metrics when k6 process completes.
/// </summary>
internal sealed class K6Executor
{
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
    /// Executes k6 with the specified script and returns metrics when complete.
    /// </summary>
    /// <param name="scriptPath">Path to k6 script file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of parsed k6 metrics.</returns>
    /// <exception cref="InvalidOperationException">k6 binary not found or execution failed.</exception>
    public async Task<List<K6Metric>> ExecuteAsync(
        string scriptPath,
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

        // Read and parse stdout (metrics)
        await foreach (var line in process.StandardOutput.ReadLinesAsync(cancellationToken))
        {
            var metric = _metricsParser.ParseLine(line);
            if (metric != null)
            {
                metrics.Add(metric);
            }
        }

        // Wait for process to exit and stderr reading to complete
        await process.WaitForExitAsync(cancellationToken);
        await stderrTask;

        var exitCode = process.ExitCode;
        _logger.LogInformation("k6 exited with code: {ExitCode}", exitCode);

        // Check for errors
        if (exitCode != 0)
        {
            var stderr = stderrBuilder.ToString();
            throw new InvalidOperationException(
                $"k6 execution failed with exit code {exitCode}. Stderr:\n{stderr}");
        }

        return metrics;
    }

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
