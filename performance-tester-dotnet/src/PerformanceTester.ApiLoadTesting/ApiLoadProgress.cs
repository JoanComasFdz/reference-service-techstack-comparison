namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Progress information for API load testing.
/// Plain data record (CODING_GUIDELINES: Explicit Over Implicit).
/// </summary>
public readonly record struct ApiLoadProgress(
    double ElapsedSeconds,
    double TotalSeconds,
    int RequestCount,
    int SuccessCount,
    int FailedCount);

/// <summary>
/// Delegate for reporting API load progress updates.
/// </summary>
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);

