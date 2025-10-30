namespace Dotnet9ReferenceService;

/// <summary>
/// Represents an instrument status record in the database.
/// </summary>
public class InstrumentStatus
{
    /// <summary>
    /// Gets or sets the unique identifier for the status record.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the device identifier.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the previous status of the instrument.
    /// </summary>
    public string PreviousStatus { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current status of the instrument.
    /// </summary>
    public string CurrentStatus { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the status change occurred.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
