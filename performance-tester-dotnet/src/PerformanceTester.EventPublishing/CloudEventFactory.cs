using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Factory for creating CloudEvents v1.0 compliant test events.
/// Uses official CloudNative.CloudEvents library.
/// </summary>
internal static class CloudEventFactory
{
    private const string EventSource = "urn:uuid:dotnet-performance-tester";
    private const string EventType = "instrument.status.changed";
    private const string ContentType = "application/json";
    private static readonly Uri SchemaUrl = new("https://github.com/jcomasfz/performance-tester/schemas/instrument-status-changed-v1.json");

    private static readonly string[] DeviceIds = Enumerable.Range(1, 999)
        .Select(i => $"DEVICE-{i:D3}")
        .ToArray();

    private static readonly (string Previous, string Current)[] StatusTransitions =
    {
        ("IDLE", "RUNNING"),
        ("RUNNING", "IDLE"),
        ("IDLE", "ERROR"),
        ("ERROR", "IDLE"),
        ("RUNNING", "ERROR"),
        ("ERROR", "RUNNING")
    };

    private static readonly CloudEventFormatter Formatter = new JsonEventFormatter();

    /// <summary>
    /// Creates a CloudEvent with specified device ID and status transition.
    /// </summary>
    /// <param name="deviceId">Device identifier (e.g., "DEVICE-001").</param>
    /// <param name="previousStatus">Previous status.</param>
    /// <param name="currentStatus">Current status.</param>
    /// <returns>CloudEvent instance conforming to v1.0 specification.</returns>
    public static CloudEvent CreateInstrumentStatusChangedEvent(
        string deviceId,
        string previousStatus,
        string currentStatus)
    {
        var cloudEvent = new CloudEvent
        {
            Id = Guid.NewGuid().ToString(),
            Source = new Uri(EventSource),
            Type = EventType,
            Time = DateTimeOffset.UtcNow,
            DataContentType = ContentType,
            DataSchema = SchemaUrl,
            Data = new
            {
                deviceId,
                previousStatus,
                currentStatus,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        return cloudEvent;
    }

    /// <summary>
    /// Creates a CloudEvent with random device ID and status transition.
    /// Used for load testing scenarios.
    /// </summary>
    /// <returns>CloudEvent with randomized test data.</returns>
    public static CloudEvent CreateRandomEvent()
    {
        var deviceId = DeviceIds[Random.Shared.Next(DeviceIds.Length)];
        var transition = StatusTransitions[Random.Shared.Next(StatusTransitions.Length)];

        return CreateInstrumentStatusChangedEvent(
            deviceId,
            transition.Previous,
            transition.Current);
    }

    /// <summary>
    /// Serializes a CloudEvent to JSON bytes for publishing.
    /// </summary>
    public static ReadOnlyMemory<byte> Serialize(CloudEvent cloudEvent)
    {
        return Formatter.EncodeStructuredModeMessage(cloudEvent, out _);
    }
}
