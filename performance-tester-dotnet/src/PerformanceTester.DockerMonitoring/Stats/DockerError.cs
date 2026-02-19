namespace PerformanceTester.DockerMonitoring.Stats;

/// <summary>
/// Error type for Docker operations. Used as the TFailure in Result&lt;T, DockerError&gt;.
/// </summary>
internal sealed record DockerError(string Message, Exception? Cause = null);
