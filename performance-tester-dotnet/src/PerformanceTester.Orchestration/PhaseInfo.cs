namespace PerformanceTester.Orchestration;

/// <summary>
/// Represents the test phases in the orchestration workflow.
/// </summary>
public enum TestPhase
{
    /// <summary>
    /// Setup phase: service discovery, infrastructure initialization.
    /// </summary>
    Setup,

    /// <summary>
    /// Warmup phase: non-measured warmup events and API calls.
    /// </summary>
    Warmup,

    /// <summary>
    /// Event test phase: measured publish/consume throughput test.
    /// </summary>
    EventTest,

    /// <summary>
    /// API test phase: measured HTTP load test.
    /// </summary>
    ApiTest,

    /// <summary>
    /// Teardown phase: disconnect event publisher, stop monitoring services.
    /// </summary>
    Teardown,

    /// <summary>
    /// Reporting phase: metrics collection, report generation.
    /// </summary>
    Reporting
}

/// <summary>
/// Represents the state of a phase transition.
/// </summary>
public enum PhaseState
{
    /// <summary>
    /// Phase is about to start.
    /// </summary>
    Starting,

    /// <summary>
    /// Phase has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Phase failed with an error.
    /// </summary>
    Failed
}

/// <summary>
/// Information about a phase transition in the test workflow.
/// Used with IProgress&lt;PhaseInfo&gt; to notify observers of phase changes.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="Message">Optional descriptive message about the phase.</param>
/// <param name="Timestamp">When the transition occurred.</param>
public readonly record struct PhaseInfo(
    TestPhase Phase,
    PhaseState State,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>
    /// Gets the timestamp, defaulting to now if not specified.
    /// </summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a PhaseInfo indicating a phase is starting.
    /// </summary>
    public static PhaseInfo Starting(TestPhase phase, string? message = null)
        => new(phase, PhaseState.Starting, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a PhaseInfo indicating a phase has completed.
    /// </summary>
    public static PhaseInfo Completed(TestPhase phase, string? message = null)
        => new(phase, PhaseState.Completed, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a PhaseInfo indicating a phase has failed.
    /// </summary>
    public static PhaseInfo Failed(TestPhase phase, string? message = null)
        => new(phase, PhaseState.Failed, message, DateTimeOffset.UtcNow);
}
