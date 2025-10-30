using System.Text.Json.Serialization;

namespace Dotnet9.Events;

/// <summary>
/// Event representing a change in instrument status.
/// Extends CloudEvent with typed data payload.
/// </summary>
public class InstrumentStatusChangedEvent : CloudEvent
{
    /// <summary>
    /// Creates a new InstrumentStatusChangedEvent with the specified data.
    /// </summary>
    /// <param name="source">The source of the event (e.g., "urn:uuid:device-simulator")</param>
    /// <param name="deviceId">The unique identifier of the device</param>
    /// <param name="previousStatus">The previous status of the instrument</param>
    /// <param name="currentStatus">The current status of the instrument</param>
    public InstrumentStatusChangedEvent(string source, string deviceId, string previousStatus, string currentStatus)
    {
        Id = Guid.NewGuid().ToString();
        Specversion = "1.0";
        Source = source;
        Type = "instrument.status.changed";
        Time = DateTime.UtcNow.ToString("O");
        Privacyrelevant = false;
        Datacontenttype = "application/json";
        Dataschema = "https://example.com/schemas/events-catalog/instrument.status.changed.schema.json";
        Kind = "event";

        Data = new Dictionary<string, object>
        {
            { "deviceId", deviceId },
            { "previousStatus", previousStatus },
            { "currentStatus", currentStatus }
        };
    }

    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public InstrumentStatusChangedEvent()
    {
    }

    /// <summary>
    /// Gets the device ID from the event data.
    /// </summary>
    /// <returns>The device ID, or null if not present</returns>
    public string? GetDeviceId()
    {
        return Data.GetValueOrDefault("deviceId")?.ToString();
    }

    /// <summary>
    /// Gets the previous status from the event data.
    /// </summary>
    /// <returns>The previous status, or null if not present</returns>
    public string? GetPreviousStatus()
    {
        return Data.GetValueOrDefault("previousStatus")?.ToString();
    }

    /// <summary>
    /// Gets the current status from the event data.
    /// </summary>
    /// <returns>The current status, or null if not present</returns>
    public string? GetCurrentStatus()
    {
        return Data.GetValueOrDefault("currentStatus")?.ToString();
    }
}
