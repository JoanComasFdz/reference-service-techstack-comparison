using System.Text.Json.Serialization;

namespace Dotnet9.Events;

/// <summary>
/// Event representing a KPI update for instrument status.
/// Extends CloudEvent with typed data payload.
/// </summary>
public class InstrumentStatusKpiUpdatedEvent : CloudEvent
{
    /// <summary>
    /// Creates a new InstrumentStatusKpiUpdatedEvent with the specified data.
    /// </summary>
    /// <param name="source">The source of the event (e.g., "urn:uuid:dotnet9")</param>
    /// <param name="deviceId">The unique identifier of the device</param>
    /// <param name="currentStatus">The current status of the instrument</param>
    public InstrumentStatusKpiUpdatedEvent(string source, string deviceId, string currentStatus)
    {
        Id = Guid.NewGuid().ToString();
        Specversion = "1.0";
        Source = source;
        Type = "instrumentstatus.kpi.updated";
        Time = DateTime.UtcNow.ToString("O");
        Privacyrelevant = false;
        Datacontenttype = "application/json";
        Dataschema = "https://example.com/schemas/events-catalog/instrumentstatus.kpi.updated.schema.json";
        Kind = "event";

        Data = new Dictionary<string, object>
        {
            { "deviceId", deviceId },
            { "currentStatus", currentStatus }
        };
    }

    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public InstrumentStatusKpiUpdatedEvent()
    {
    }
}
