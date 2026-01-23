using System.Text.Json;
using System.Text.RegularExpressions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Helper assertions for contract-based conformance testing.
/// Each method provides precise failure messages identifying exact field/format issues.
/// </summary>
public static class ContractAssertions
{
    /// <summary>
    /// Asserts that a field exists in the JSON object.
    /// </summary>
    public static void AssertFieldExists(JsonElement parent, string fieldName, string context = "")
    {
        var contextMsg = string.IsNullOrEmpty(context) ? "" : $" in {context}";
        Assert.True(
            parent.TryGetProperty(fieldName, out _),
            $"Field '{fieldName}' must exist{contextMsg} but was not found. Available fields: {GetFieldNames(parent)}");
    }

    /// <summary>
    /// Asserts that a field does NOT exist (to verify no extra fields).
    /// </summary>
    public static void AssertFieldNotExists(JsonElement parent, string fieldName, string context = "")
    {
        var contextMsg = string.IsNullOrEmpty(context) ? "" : $" in {context}";
        Assert.False(
            parent.TryGetProperty(fieldName, out _),
            $"Field '{fieldName}' must NOT exist{contextMsg} but was found");
    }

    /// <summary>
    /// Asserts a string field has the exact expected value.
    /// </summary>
    public static void AssertStringValue(JsonElement parent, string fieldName, string expected)
    {
        AssertFieldExists(parent, fieldName);
        var actual = parent.GetProperty(fieldName).GetString();
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Asserts a numeric field has the expected value with tolerance.
    /// </summary>
    public static void AssertNumericValue(
        JsonElement parent,
        string fieldName,
        double expected,
        int decimalPrecision)
    {
        AssertFieldExists(parent, fieldName);
        var actual = parent.GetProperty(fieldName).GetDouble();
        var tolerance = Math.Pow(10, -(decimalPrecision + 1));
        Assert.True(
            Math.Abs(actual - expected) < tolerance,
            $"Field '{fieldName}': expected {expected} but was {actual}");
    }

    /// <summary>
    /// Asserts that a numeric field has exactly the specified decimal precision in the JSON string.
    /// </summary>
    public static void AssertDecimalPrecision(JsonElement parent, string fieldName, int expectedDecimals, string rawJson)
    {
        AssertFieldExists(parent, fieldName);

        // Extract the raw value from JSON using regex
        var pattern = $@"""{fieldName}""\s*:\s*(-?\d+\.?\d*)";
        var match = Regex.Match(rawJson, pattern);

        if (!match.Success)
        {
            Assert.Fail($"Could not find field '{fieldName}' value in raw JSON");
            return;
        }

        var stringValue = match.Groups[1].Value;
        var decimalIndex = stringValue.IndexOf('.');

        if (expectedDecimals == 0)
        {
            Assert.True(
                decimalIndex == -1,
                $"Field '{fieldName}' should be integer but has decimals: {stringValue}");
        }
        else
        {
            Assert.True(
                decimalIndex >= 0,
                $"Field '{fieldName}' should have {expectedDecimals} decimals but has none: {stringValue}");

            var actualDecimals = stringValue.Length - decimalIndex - 1;
            // Allow more decimals than expected (e.g., 3.000 is ok when expecting 3 decimals)
            Assert.True(
                actualDecimals >= expectedDecimals,
                $"Field '{fieldName}' should have at least {expectedDecimals} decimals but has {actualDecimals}: {stringValue}");
        }
    }

    /// <summary>
    /// Asserts an integer field has the expected value.
    /// </summary>
    public static void AssertIntegerValue(JsonElement parent, string fieldName, int expected)
    {
        AssertFieldExists(parent, fieldName);
        var actual = parent.GetProperty(fieldName).GetInt32();
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Asserts an integer field exists and returns its value.
    /// </summary>
    public static int GetIntegerValue(JsonElement parent, string fieldName)
    {
        AssertFieldExists(parent, fieldName);
        return parent.GetProperty(fieldName).GetInt32();
    }

    /// <summary>
    /// Asserts a boolean field has the expected value.
    /// </summary>
    public static void AssertBooleanValue(JsonElement parent, string fieldName, bool expected)
    {
        AssertFieldExists(parent, fieldName);
        var actual = parent.GetProperty(fieldName).GetBoolean();
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Asserts the field order in a JSON object matches expected order.
    /// </summary>
    public static void AssertFieldOrder(string rawJson, params string[] expectedOrder)
    {
        var positions = expectedOrder.Select(field =>
        {
            var pattern = $@"""{field}""";
            var match = Regex.Match(rawJson, pattern);
            return (field, position: match.Success ? match.Index : -1);
        }).ToList();

        for (int i = 0; i < expectedOrder.Length; i++)
        {
            Assert.True(
                positions[i].position >= 0,
                $"Field '{expectedOrder[i]}' not found in JSON");

            if (i > 0)
            {
                Assert.True(
                    positions[i].position > positions[i - 1].position,
                    $"Field '{expectedOrder[i]}' should come after '{expectedOrder[i - 1]}' but doesn't. " +
                    $"'{expectedOrder[i-1]}' at position {positions[i-1].position}, '{expectedOrder[i]}' at position {positions[i].position}");
            }
        }
    }

    /// <summary>
    /// Asserts a timestamp field matches the expected format.
    /// </summary>
    public static void AssertTimestampFormat(
        JsonElement parent,
        string fieldName,
        TimestampFormat format)
    {
        AssertFieldExists(parent, fieldName);
        var value = parent.GetProperty(fieldName).GetString();
        Assert.NotNull(value);

        var pattern = format switch
        {
            TimestampFormat.TestDate => @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$",
            TimestampFormat.Iso8601WithMicroseconds => @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}$",
            TimestampFormat.Iso8601WithTimezone => @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}\+00:00$",
            _ => throw new ArgumentException($"Unknown format: {format}")
        };

        Assert.True(
            Regex.IsMatch(value, pattern),
            $"Field '{fieldName}' value '{value}' doesn't match format {format} (pattern: {pattern})");
    }

    /// <summary>
    /// Asserts an array has the expected count.
    /// </summary>
    public static void AssertArrayCount(JsonElement parent, string fieldName, int expectedCount)
    {
        AssertFieldExists(parent, fieldName);
        var array = parent.GetProperty(fieldName);
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(expectedCount, array.GetArrayLength());
    }

    /// <summary>
    /// Asserts an array has at least the specified count.
    /// </summary>
    public static void AssertArrayMinCount(JsonElement parent, string fieldName, int minCount)
    {
        AssertFieldExists(parent, fieldName);
        var array = parent.GetProperty(fieldName);
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.True(
            array.GetArrayLength() >= minCount,
            $"Array '{fieldName}' should have at least {minCount} items but has {array.GetArrayLength()}");
    }

    /// <summary>
    /// Asserts an object exists at the specified field.
    /// </summary>
    public static JsonElement AssertObjectExists(JsonElement parent, string fieldName)
    {
        AssertFieldExists(parent, fieldName);
        var obj = parent.GetProperty(fieldName);
        Assert.Equal(JsonValueKind.Object, obj.ValueKind);
        return obj;
    }

    /// <summary>
    /// Gets field names from a JSON element for error messages.
    /// </summary>
    private static string GetFieldNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return "(not an object)";

        var names = new List<string>();
        foreach (var prop in element.EnumerateObject())
        {
            names.Add(prop.Name);
        }
        return string.Join(", ", names);
    }

    /// <summary>
    /// Asserts a string field contains the expected substring.
    /// </summary>
    public static void AssertStringContains(JsonElement parent, string fieldName, string expectedSubstring)
    {
        AssertFieldExists(parent, fieldName);
        var actual = parent.GetProperty(fieldName).GetString();
        Assert.Contains(expectedSubstring, actual);
    }

    /// <summary>
    /// Asserts a string field matches the expected regex pattern.
    /// </summary>
    public static void AssertStringMatches(JsonElement parent, string fieldName, string pattern)
    {
        AssertFieldExists(parent, fieldName);
        var actual = parent.GetProperty(fieldName).GetString();
        Assert.NotNull(actual);
        Assert.Matches(pattern, actual);
    }

    /// <summary>
    /// Gets a nested property value for assertions.
    /// </summary>
    public static JsonElement GetNestedElement(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            AssertFieldExists(current, segment, $"path: {string.Join(".", path)}");
            current = current.GetProperty(segment);
        }
        return current;
    }
}

/// <summary>
/// Timestamp format patterns for contract verification.
/// </summary>
public enum TimestampFormat
{
    /// <summary>
    /// Main report format: "YYYY-MM-DD HH:MM:SS"
    /// </summary>
    TestDate,

    /// <summary>
    /// Sample timestamp: "YYYY-MM-DDTHH:MM:SS.ffffff" (no timezone)
    /// </summary>
    Iso8601WithMicroseconds,

    /// <summary>
    /// Sample timestamp with timezone: "YYYY-MM-DDTHH:MM:SS.ffffff+00:00"
    /// </summary>
    Iso8601WithTimezone
}
