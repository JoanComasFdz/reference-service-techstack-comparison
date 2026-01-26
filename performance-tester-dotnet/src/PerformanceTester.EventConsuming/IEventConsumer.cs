namespace PerformanceTester.EventConsuming;

/// <summary>
/// Service for consuming CloudEvents from RabbitMQ with event tracking and inactivity timeout.
/// Implemented as a BackgroundService that runs continuously until stopped.
/// </summary>
public interface IEventConsumer
{
    /// <summary>
    /// Establishes connection to RabbitMQ and starts consuming events.
    /// Must be called before StartTrackingEventsAsync.
    /// </summary>
    /// <param name="progress">Optional progress reporter for phase transitions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Connection failed or already connected.</exception>
    /// <remarks>
    /// Reports phases: Connecting/Starting → ConsumerRegistered/Completed → Connected/Completed
    /// </remarks>
    Task ConnectAsync(
        IProgress<ConsumerPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects from RabbitMQ.
    /// Safe to call multiple times.
    /// </summary>
    /// <param name="progress">Optional progress reporter for phase transitions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(
        IProgress<ConsumerPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts tracking events until the expected count is reached or inactivity timeout expires.
    /// This method returns a Task that completes when:
    /// - Expected count is reached (success)
    /// - Inactivity timeout expires (throws TimeoutException)
    /// - Cancellation is requested (throws OperationCanceledException)
    /// </summary>
    /// <param name="expectedCount">Number of events to wait for.</param>
    /// <param name="inactivityTimeout">Maximum time allowed since last event received (default: 120s).</param>
    /// <param name="progress">Optional progress reporter for phase transitions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when expected count is reached or timeout expires.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Expected count is less than 1 or timeout is negative.</exception>
    /// <exception cref="TimeoutException">Inactivity timeout expired (no events received for specified duration).</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    /// <remarks>
    /// Reports phases: TrackingStarted/Starting → EventReceived/Completed (per event) → TargetReached/Completed
    ///
    /// IMPORTANT: Timeout resets on EVERY event received (inactivity timeout, not absolute timeout).
    /// This is an improvement over the Python implementation which used absolute timeout.
    /// Allows slow-but-progressing services to complete while detecting truly stuck services.
    /// </remarks>
    Task StartTrackingEventsAsync(
        int expectedCount,
        TimeSpan inactivityTimeout,
        IProgress<ConsumerPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default);
}
