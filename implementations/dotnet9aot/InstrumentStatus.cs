using System.Diagnostics.CodeAnalysis;

namespace Dotnet9ReferenceServiceAoT;

// Annotate type to preserve all properties for Dapper mapping
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class InstrumentStatus
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
