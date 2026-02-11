using System.Text;
using System.Text.Json;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.IntegrationTests.Shared.Utilities;

public class Iso8601DateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new Iso8601DateTimeConverter() }
    };

    [Fact]
    public void Write_UtcDateTime_ShouldPreserveValue()
    {
        var utcTime = new DateTime(2026, 2, 10, 12, 17, 32, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(utcTime, Options);

        Assert.Equal("\"2026-02-10T12:17:32.000000\"", json);
    }

    [Fact]
    public void Write_UnspecifiedDateTime_ShouldPreserveValue()
    {
        // This is the bug: Unspecified Kind should NOT be shifted by timezone offset.
        // DateTimeOffset.DateTime returns Unspecified, but the value is already UTC.
        var unspecifiedTime = new DateTime(2026, 2, 10, 12, 17, 32, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(unspecifiedTime, Options);

        Assert.Equal("\"2026-02-10T12:17:32.000000\"", json);
    }

    [Fact]
    public void Write_UtcAndUnspecified_ShouldProduceSameOutput()
    {
        var utcTime = new DateTime(2026, 2, 10, 12, 17, 32, DateTimeKind.Utc);
        var unspecifiedTime = new DateTime(2026, 2, 10, 12, 17, 32, DateTimeKind.Unspecified);

        var utcJson = JsonSerializer.Serialize(utcTime, Options);
        var unspecifiedJson = JsonSerializer.Serialize(unspecifiedTime, Options);

        Assert.Equal(utcJson, unspecifiedJson);
    }

    [Fact]
    public void ReadThenWrite_ShouldRoundTrip()
    {
        var original = new DateTime(2026, 2, 10, 12, 17, 32, 500, DateTimeKind.Utc);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DateTime>(json, Options);
        var rejson = JsonSerializer.Serialize(deserialized, Options);

        Assert.Equal(json, rejson);
    }
}
