using System.Text.Json.Serialization;
using Dotnet9.Events;

namespace Dotnet9ReferenceServiceAoT;

[JsonSerializable(typeof(List<InstrumentStatus>))]
[JsonSerializable(typeof(InstrumentStatus))]
[JsonSerializable(typeof(CloudEvent))]
[JsonSerializable(typeof(InstrumentStatusChangedEvent))]
[JsonSerializable(typeof(InstrumentStatusKpiUpdatedEvent))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
