using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerformanceTester.Reporting.Shared.Utilities;

/// <summary>
/// Custom JSON converter for DateTime that formats timestamps in ISO 8601 format without timezone.
/// Used for sample timestamps in throughput and resource metrics reports.
/// </summary>
/// <remarks>
/// Format: yyyy-MM-ddTHH:mm:ss.ffffff
/// Example: 2025-11-13T14:25:30.100000
///
/// This differs from the main report timestamp format (yyyy-MM-dd HH:mm:ss)
/// which is handled separately in the ReportGenerator.
/// 
/// Note: No timezone offset to match Python output format.
/// </remarks>
internal sealed class Iso8601DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Format: yyyy-MM-ddTHH:mm:ss.ffffff (no timezone)
        // All timestamps in this codebase are UTC. We do NOT call .ToUniversalTime()
        // because DateTime values with Kind=Unspecified (from DateTimeOffset.DateTime
        // or DateTime.Parse) would be incorrectly shifted by the local timezone offset.
        var formatted = value.ToString("yyyy-MM-ddTHH:mm:ss.ffffff");
        writer.WriteStringValue(formatted);
    }
}
