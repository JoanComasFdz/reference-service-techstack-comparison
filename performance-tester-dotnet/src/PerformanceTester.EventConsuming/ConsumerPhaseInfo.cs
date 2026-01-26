namespace PerformanceTester.EventConsuming;

/// <summary>
/// Represents the phases in the event consuming workflow.
/// </summary>
public enum ConsumerPhase
{
    /// <summary>
    /// Establishing connection to RabbitMQ.
    /// </summary>
    Connecting,

    /// <summary>
    /// Successfully connected to RabbitMQ.
    /// </summary>
    Connected,

    /// <summary>
    /// Consumer registered and ready to receive messages.
    /// This is the safe point to start publishing events.
    /// </summary>
    ConsumerRegistered,

    /// <summary>
    /// Event tracking has started (waiting for expected count).
    /// </summary>
    TrackingStarted,

    /// <summary>
    /// An event was received and processed.
    /// </summary>
    EventReceived,

    /// <summary>
    /// Target event count has been reached.
    /// </summary>
    TargetReached,

    /// <summary>
    /// Disconnecting from RabbitMQ.
    /// </summary>
    Disconnecting
}

/// <summary>
/// Represents the state of a consumer phase transition.
/// </summary>
public enum ConsumerPhaseState
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
/// Information about a phase transition in the event consuming workflow.
/// Used with IProgress&lt;ConsumerPhaseInfo&gt; to notify observers of state changes.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="EventCount">Current received event count (for EventReceived/TargetReached phases).</param>
/// <param name="Message">Optional descriptive message about the phase.</param>
/// <param name="Timestamp">When the transition occurred.</param>
public readonly record struct ConsumerPhaseInfo(
    ConsumerPhase Phase,
    ConsumerPhaseState State,
    int? EventCount = null,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>
    /// Gets the timestamp, defaulting to now if not specified.
    /// </summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a ConsumerPhaseInfo indicating a phase is starting.
    /// </summary>
    public static ConsumerPhaseInfo Starting(ConsumerPhase phase, string? message = null, int? eventCount = null)
        => new(phase, ConsumerPhaseState.Starting, eventCount, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a ConsumerPhaseInfo indicating a phase has completed.
    /// </summary>
    public static ConsumerPhaseInfo Completed(ConsumerPhase phase, string? message = null, int? eventCount = null)
        => new(phase, ConsumerPhaseState.Completed, eventCount, message, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a ConsumerPhaseInfo indicating a phase has failed.
    /// </summary>
    public static ConsumerPhaseInfo Failed(ConsumerPhase phase, string? message = null, int? eventCount = null)
        => new(phase, ConsumerPhaseState.Failed, eventCount, message, DateTimeOffset.UtcNow);
}
