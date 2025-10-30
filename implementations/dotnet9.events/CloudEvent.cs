using System.Text.Json.Serialization;

namespace Dotnet9.Events;

/// <summary>
/// Base class for CloudEvents representing events in the event catalog.
/// Implements the CloudEvents specification with extension attributes.
/// </summary>
public class CloudEvent
{
    /// <summary>
    /// Gets or sets the unique identifier for the event.
    /// This value is generated automatically and should be unique across all events.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the CloudEvents specification.
    /// Typically set to "1.0" for CloudEvents 1.0 specification compliance.
    /// </summary>
    [JsonPropertyName("specversion")]
    public string Specversion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source of the event.
    /// Identifies the context in which the event occurred (e.g., "urn:uuid:device-simulator").
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the event.
    /// Describes the event type in a domain-specific format (e.g., "instrument.status.changed").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the event occurred.
    /// Should be in ISO 8601 format (e.g., "2025-01-01T12:00:00.000Z").
    /// </summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the event contains privacy-relevant data.
    /// When true, special handling may be required for data protection compliance.
    /// This is a CloudEvents extension attribute.
    /// </summary>
    [JsonPropertyName("privacyrelevant")]
    public bool Privacyrelevant { get; set; }

    /// <summary>
    /// Gets or sets the content type of the data payload.
    /// Typically set to "application/json" for JSON-encoded data.
    /// </summary>
    [JsonPropertyName("datacontenttype")]
    public string Datacontenttype { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URI reference to the schema that the data adheres to.
    /// Points to the JSON schema definition for this event type in the events catalog.
    /// </summary>
    [JsonPropertyName("dataschema")]
    public string Dataschema { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of CloudEvents message.
    /// Typically set to "event" to indicate this is an event notification.
    /// This is a CloudEvents extension attribute.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event payload data.
    /// Contains the domain-specific event data as key-value pairs.
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; } = new();
}
