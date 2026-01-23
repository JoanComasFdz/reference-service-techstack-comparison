namespace PerformanceTester.Reporting;

/// <summary>
/// Information about a monitored Docker container.
/// </summary>
public sealed record ContainerInfo
{
    /// <summary>
    /// Container name (e.g., "performancetest-rabbitmq").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Container ID (short form, e.g., "02283bd99d8f").
    /// </summary>
    public required string Id { get; init; }
}
