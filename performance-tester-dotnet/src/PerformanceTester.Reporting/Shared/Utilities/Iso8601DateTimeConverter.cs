using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerformanceTester.Reporting.Shared.Utilities;

/// <summary>
/// Custom JSON converter for DateTime that formats timestamps in ISO 8601 format with timezone.
/// Used for sample timestamps in throughput and resource metrics reports.
/// </summary>
/// <remarks>
/// Format: yyyy-MM-ddTHH:mm:ss.ffffff+00:00
/// Example: 2025-11-13T14:25:30.100000+00:00
///
/// This differs from the main report timestamp format (yyyy-MM-dd HH:mm:ss)
/// which is handled separately in the ReportGenerator.
/// </remarks>
internal sealed class Iso8601DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Ensure UTC
        var utcValue = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        // Format: yyyy-MM-ddTHH:mm:ss.ffffff+00:00
        // This format includes:
        // - T separator between date and time
        // - 6-digit fractional seconds
        // - Timezone offset (+00:00 for UTC)
        var formatted = utcValue.ToString("yyyy-MM-ddTHH:mm:ss.ffffff+00:00");
        writer.WriteStringValue(formatted);
    }
}
