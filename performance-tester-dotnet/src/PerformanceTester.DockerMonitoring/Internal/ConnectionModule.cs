using Dunet;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Internal;

// ── Union Types (namespace-level, required by Dunet source generator) ────────

/// <summary>
/// Explicit state machine for Docker streaming connection lifecycle.
/// Replaces implicit state in volatile fields (_consecutiveFailures, _hasReceivedValidStats,
/// _streamingFailed, _currentBackoffDelay).
/// </summary>
/// <remarks>
/// Uses Dunet [Union] for exhaustive Match() support in single-dispatch contexts.
/// The state machine transition function (<see cref="ConnectionModule.StateMachine.Transition"/>)
/// uses native C# switch expressions instead of Match() because it matches on
/// (state, event) tuples with 'when' guards — a pattern that Match() cannot express.
/// Part of <see cref="ConnectionModule"/> (kept at namespace level for Dunet compatibility).
/// </remarks>
[Union]
internal partial record ConnectionState
{
    /// <summary>
    /// Attempting to connect to Docker stats stream.
    /// </summary>
    partial record Connecting(
        ContainerId ContainerId,
        ConnectionModule.AttemptCount AttemptNumber,
        TimeSpan NextBackoff);

    /// <summary>
    /// Successfully receiving stats from Docker.
    /// </summary>
    partial record Connected(
        ContainerId ContainerId);

    /// <summary>
    /// Connection lost, will retry after waiting <see cref="NextBackoff"/>.
    /// </summary>
    partial record Disconnected(
        ContainerId ContainerId,
        ConnectionModule.AttemptCount ConsecutiveFailures,
        TimeSpan NextBackoff,
        Exception LastError);

    /// <summary>
    /// Connection failed permanently (max retries exceeded).
    /// </summary>
    partial record Failed(
        ConnectionModule.AttemptCount TotalAttempts,
        Exception LastError);
}

/// <summary>
/// Events that can occur during a Docker stats streaming session.
/// Used as input to <see cref="ConnectionModule.StateMachine.Transition"/>.
/// Part of <see cref="ConnectionModule"/> (kept at namespace level for Dunet compatibility).
/// </summary>
[Union]
internal partial record StreamEvent
{
    /// <summary>Valid stats were received from the stream.</summary>
    partial record StatsReceived;

    /// <summary>An error occurred in the stream.</summary>
    partial record Error(Exception Exception);

    /// <summary>The stream was cancelled (normal shutdown).</summary>
    partial record Cancelled;
}

// ── Module ───────────────────────────────────────────────────────────────────

/// <summary>
/// FP-style module for Docker streaming connection lifecycle (Guideline 02-05).
/// Owns StateMachine (pure transitions), ReconnectionPolicy (backoff),
/// StreamingConstants (config), and AttemptCount (value object).
/// The Dunet union types (<see cref="ConnectionState"/>, <see cref="StreamEvent"/>)
/// live at namespace level for source-generator compatibility but are logically part of this module.
/// </summary>
internal static class ConnectionModule
{
    // ── Value Objects ─────────────────────────────────────────────────────────

    /// <summary>
    /// Value object for connection attempt/failure counts (>= 0).
    /// Used across <see cref="ConnectionState"/> variants: AttemptNumber, ConsecutiveFailures, TotalAttempts.
    /// These represent the same counter flowing through the state machine lifecycle.
    /// </summary>
    internal sealed record AttemptCount : NonNegativeInt
    {
        private AttemptCount(int value) : base(value) { }

        public static Result<AttemptCount, string> Create(int value) => Create(value, "Attempt count", v => new AttemptCount(v));

        public static AttemptCount FromInt(int value) => new(value);

        // -- Type-preserving arithmetic (shadows base NonNegativeInt operators) --

        public static AttemptCount operator +(AttemptCount left, int right) => new(Math.Max(0, left.Value + right));

        public static AttemptCount operator -(AttemptCount left, int right) => new(Math.Max(0, left.Value - right));

        public static AttemptCount operator ++(AttemptCount value) => new(value.Value + 1);

        public static AttemptCount operator --(AttemptCount value) => new(Math.Max(0, value.Value - 1));
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Constants for Docker streaming mode configuration.
    /// </summary>
    internal static class StreamingConstants
    {
        /// <summary>
        /// Timeout waiting for first stats from Docker stream.
        /// Docker typically pushes first stats within 1-2 seconds.
        /// </summary>
        public static readonly TimeSpan FirstStatsTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Timeout for waiting on streaming task during shutdown.
        /// </summary>
        public static readonly TimeSpan StreamingShutdownTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Initial delay before first reconnection attempt.
        /// </summary>
        public static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between reconnection attempts.
        /// </summary>
        public static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum consecutive failures before giving up.
        /// Resets to 0 when connection succeeds (first valid stats received).
        /// </summary>
        public static readonly AttemptCount MaxReconnectAttempts = AttemptCount.FromInt(10);

        /// <summary>
        /// Represents the default maximum jitter duration, in milliseconds, used for randomized delays.
        /// </summary>
        /// <remarks>This value can be used as a standard upper bound for jitter when introducing randomized
        /// backoff or delay in operations. The value is set to 500 milliseconds.</remarks>
        public static readonly JitterMaxMilliseconds JitterMaxMilliseconds = JitterMaxMilliseconds.FromInt(500);
    }

    // ── State Machine ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pure state transition function for Docker streaming connection lifecycle.
    /// Given (current state, event) → next state. No side effects.
    /// </summary>
    internal static class StateMachine
    {
        /// <summary>
        /// Computes the next connection state given the current state and a stream event.
        /// </summary>
        /// <remarks>
        /// Uses native C# switch expression instead of Dunet's Match() because:
        /// 1. Tuple pattern matching — matches (state, event) pairs simultaneously
        /// 2. 'when' guards — splits same (state, event) pair by runtime condition
        /// 3. Flat transition table — each line is one rule, reads like a state diagram
        /// Match() would require nested calls (state.Match → event.Match) with ternaries
        /// replacing guards, obscuring the state machine structure.
        /// </remarks>
        public static ConnectionState Transition(
            ConnectionState current,
            StreamEvent streamEvent,
            NonNegativeInt maxReconnectAttempts,
            TimeSpan maxReconnectDelay) => (current, streamEvent) switch
        {
            // Connecting + valid stats → Connected
            (ConnectionState.Connecting c, StreamEvent.StatsReceived) => new ConnectionState.Connected(c.ContainerId),

            // Connecting + error (under limit) → Disconnected
            (ConnectionState.Connecting c, StreamEvent.Error e) when ReconnectionPolicy.ShouldRetry(c.AttemptNumber + 1, maxReconnectAttempts) =>
                new ConnectionState.Disconnected(
                    c.ContainerId,
                    c.AttemptNumber + 1,
                    c.NextBackoff,
                    e.Exception),

            // Connecting + error (at limit) → Failed
            (ConnectionState.Connecting c, StreamEvent.Error e) =>
                new ConnectionState.Failed(c.AttemptNumber + 1, e.Exception),

            // Connected + error → Disconnected (reset to count=1, initial backoff)
            (ConnectionState.Connected c, StreamEvent.Error e) => new ConnectionState.Disconnected(
                    c.ContainerId,
                    AttemptCount.FromInt(1),
                    StreamingConstants.InitialReconnectDelay,
                    e.Exception),

            // Disconnected + valid stats → Connected (reset)
            (ConnectionState.Disconnected d, StreamEvent.StatsReceived) =>
                new ConnectionState.Connected(d.ContainerId),

            // Disconnected + error (under limit) → Disconnected with incremented count
            (ConnectionState.Disconnected d, StreamEvent.Error e)
                when ReconnectionPolicy.ShouldRetry(d.ConsecutiveFailures + 1, maxReconnectAttempts) =>
                d with
                {
                    ConsecutiveFailures = d.ConsecutiveFailures + 1,
                    NextBackoff = ReconnectionPolicy.CalculateBackoff(d.NextBackoff, maxReconnectDelay).NextBackoff,
                    LastError = e.Exception
                },

            // Disconnected + error (at limit) → Failed
            (ConnectionState.Disconnected d, StreamEvent.Error e) => new ConnectionState.Failed(d.ConsecutiveFailures + 1, e.Exception),

            // Any state + Cancelled → no-op
            (_, StreamEvent.Cancelled) => current,

            // Default: no-op (safety net)
            _ => current
        };
    }

    // ── Reconnection Policy ───────────────────────────────────────────────────

    /// <summary>
    /// Pure functions for reconnection backoff and retry decisions.
    /// No side effects, no state — all inputs explicit, all outputs via return value.
    /// </summary>
    internal static class ReconnectionPolicy
    {
        public readonly record struct BackoffResult(
            TimeSpan DelayToUse,
            TimeSpan NextBackoff);

        /// <summary>
        /// Calculates the current delay and the next backoff using exponential doubling with jitter.
        /// Returns <paramref name="currentBackoff"/> as the delay to use now,
        /// and min(currentBackoff * 2, maxBackoff) + jitter as the next backoff.
        /// </summary>
        public static BackoffResult CalculateBackoff(
            TimeSpan currentBackoff,
            TimeSpan maxBackoff) => CalculateBackoff(currentBackoff, maxBackoff, StreamingConstants.JitterMaxMilliseconds);

        public static BackoffResult CalculateBackoff(
            TimeSpan currentBackoff,
            TimeSpan maxBackoff,
            JitterMaxMilliseconds jitterMaxMs)
        {
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, jitterMaxMs.Value));
            var nextBackoff = TimeSpan.FromTicks(Math.Min(currentBackoff.Ticks * 2, maxBackoff.Ticks)) + jitter;
            return new(currentBackoff, nextBackoff);
        }

        /// <summary>
        /// Returns true if retrying is allowed (failures have not exceeded the limit).
        /// </summary>
        public static bool ShouldRetry(NonNegativeInt consecutiveFailures, NonNegativeInt maxAttempts) => consecutiveFailures <= maxAttempts;
    }
}
