using Microsoft.Extensions.Logging;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Service that runs k6 load tests and returns aggregated metrics.
/// Implements IApiLoadTester interface.
/// </summary>
internal sealed class ApiLoadTestService : IApiLoadTester
{
    private readonly ILogger<ApiLoadTestService> _logger;

    public ApiLoadTestService(ILogger<ApiLoadTestService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApiLoadTestResult> StartTestAsync(
        string targetUrl,
        TimeSpan duration,
        int virtualUsers,
        int maxConsecutiveFailures = 3,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        if (string.IsNullOrWhiteSpace(targetUrl))
            throw new ArgumentException("Target URL cannot be null or empty", nameof(targetUrl));

        if (!Uri.IsWellFormedUriString(targetUrl, UriKind.Absolute))
            throw new ArgumentException($"Invalid URL: {targetUrl}", nameof(targetUrl));

        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be positive", nameof(duration));

        if (virtualUsers < 1)
            throw new ArgumentException("Virtual users must be at least 1", nameof(virtualUsers));

        if (maxConsecutiveFailures < 0)
            throw new ArgumentException("Max consecutive failures cannot be negative", nameof(maxConsecutiveFailures));

        _logger.LogInformation("=== Starting API load test: {Url}, duration: {Duration}, VUs: {VUs}, maxConsecutiveFailures: {MaxFailures} ===",
            targetUrl, duration, virtualUsers, maxConsecutiveFailures);

        // Generate k6 script
        var durationString = K6ScriptGenerator.FormatDuration(duration);
        var scriptContent = K6ScriptGenerator.GenerateScript(targetUrl, durationString, virtualUsers, maxConsecutiveFailures);

        // Write script to temporary file
        var scriptPath = Path.Combine(Path.GetTempPath(), $"k6-script-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);
        _logger.LogInformation("✓ Generated k6 script: {ScriptPath}", scriptPath);

        try
        {
            // Execute k6 and get metrics
            var executor = new K6Executor(_logger);
            var testStartTime = DateTime.UtcNow;
            var executionResult = await executor.ExecuteAsync(scriptPath, cancellationToken);
            var testEndTime = DateTime.UtcNow;
            var actualDuration = testEndTime - testStartTime;

            _logger.LogInformation("✓ k6 execution completed, processing {Count} metrics (aborted: {WasAborted})",
                executionResult.Metrics.Count, executionResult.WasAborted);

            // Aggregate metrics
            var aggregator = new MetricsAggregator();
            foreach (var metric in executionResult.Metrics)
            {
                aggregator.ProcessMetric(metric);
            }

            var result = aggregator.ComputeResult(actualDuration) with
            {
                WasAborted = executionResult.WasAborted,
                AbortReason = executionResult.AbortReason
            };

            if (executionResult.WasAborted)
            {
                _logger.LogWarning("⚠️ Test aborted: {TotalRequests} requests, {FailedRequests} failed, reason: {AbortReason}",
                    result.TotalRequests, result.FailedRequests, executionResult.AbortReason);
            }
            else
            {
                _logger.LogInformation("✓ Test completed: {TotalRequests} requests, {FailedRequests} failed",
                    result.TotalRequests, result.FailedRequests);
            }

            return result;
        }
        finally
        {
            // Cleanup script file
            if (File.Exists(scriptPath))
            {
                try
                {
                    File.Delete(scriptPath);
                    _logger.LogDebug("✓ Deleted temporary script: {ScriptPath}", scriptPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to delete temporary script: {ScriptPath}", scriptPath);
                }
            }
        }
    }
}
