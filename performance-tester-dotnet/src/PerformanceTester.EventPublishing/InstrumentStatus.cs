using Dunet;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Discriminated union representing an instrument status.
/// Dunet union provides exhaustive <see cref="Match"/> — missing cases are compile-time errors.
/// </summary>
[Union]
internal partial record InstrumentStatus
{
    /// <summary>Device is idle.</summary>
    partial record Idle;

    /// <summary>Device is running.</summary>
    partial record Running;

    /// <summary>Device has encountered an error.</summary>
    partial record Error;
}

/// <summary>
/// Serialization support for <see cref="InstrumentStatus"/>.
/// </summary>
internal static class InstrumentStatusExtensions
{
    /// <summary>
    /// Returns the string value for CloudEvent serialization.
    /// Exhaustive match — adding a new variant produces a compile-time error here.
    /// </summary>
    internal static string ToValue(this InstrumentStatus status) => status.Match(
        idle => "IDLE",
        running => "RUNNING",
        error => "ERROR");
}
