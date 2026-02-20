namespace PerformanceTester.EventPublishing.CloudEvents;

/// <summary>
/// Value object representing a transition between two instrument statuses.
/// </summary>
internal sealed record StatusTransition(InstrumentStatus Previous, InstrumentStatus Current);
