using FluentAssertions;
using JoanComasFdz.Result;
using PerformanceTester.Cli.Configuration;
using Xunit;

namespace PerformanceTester.Cli.Tests.Configuration;

public class DurationParserTests
{
    #region Parse - Valid Seconds

    [Theory]
    [InlineData("1s", 1)]
    [InlineData("30s", 30)]
    [InlineData("60s", 60)]
    [InlineData("120s", 120)]
    [InlineData("3600s", 3600)]
    public void Parse_ValidSeconds_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        var result = DurationParser.Parse(input);

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    #endregion

    #region Parse - Valid Minutes

    [Theory]
    [InlineData("1m", 1)]
    [InlineData("5m", 5)]
    [InlineData("30m", 30)]
    [InlineData("60m", 60)]
    public void Parse_ValidMinutes_ReturnsCorrectTimeSpan(string input, int expectedMinutes)
    {
        var result = DurationParser.Parse(input);

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    #endregion

    #region Parse - Valid Hours

    [Theory]
    [InlineData("1h", 1)]
    [InlineData("2h", 2)]
    [InlineData("24h", 24)]
    public void Parse_ValidHours_ReturnsCorrectTimeSpan(string input, int expectedHours)
    {
        var result = DurationParser.Parse(input);

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromHours(expectedHours));
    }

    #endregion

    #region Parse - Case Insensitivity

    [Theory]
    [InlineData("30S", 30)]
    [InlineData("5M", 5)]
    [InlineData("2H", 2)]
    public void Parse_CaseInsensitive_ReturnsCorrectTimeSpan(string input, int expectedValue)
    {
        var result = DurationParser.Parse(input);

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);

        var unit = input.ToLowerInvariant().Last();
        var expected = unit switch
        {
            's' => TimeSpan.FromSeconds(expectedValue),
            'm' => TimeSpan.FromMinutes(expectedValue),
            'h' => TimeSpan.FromHours(expectedValue),
            _ => throw new InvalidOperationException($"Unknown unit: {unit}")
        };

        success.Value.Should().Be(expected);
    }

    [Fact]
    public void Parse_UppercaseSeconds_ReturnsCorrectTimeSpan()
    {
        var result = DurationParser.Parse("30S");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Parse_UppercaseMinutes_ReturnsCorrectTimeSpan()
    {
        var result = DurationParser.Parse("5M");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Parse_UppercaseHours_ReturnsCorrectTimeSpan()
    {
        var result = DurationParser.Parse("2H");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromHours(2));
    }

    #endregion

    #region Parse - Whitespace Handling

    [Theory]
    [InlineData("  30s", 30)]
    [InlineData("30s  ", 30)]
    [InlineData("  30s  ", 30)]
    [InlineData("\t30s\t", 30)]
    [InlineData(" \t 30s \t ", 30)]
    public void Parse_WithWhitespace_TrimsAndReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        var result = DurationParser.Parse(input);

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    #endregion

    #region Parse - Null/Empty Input

    [Fact]
    public void Parse_NullInput_ReturnsEmpty()
    {
        var result = DurationParser.Parse(null!);

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.Empty>(failure.Error);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var result = DurationParser.Parse("");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.Empty>(failure.Error);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        var result = DurationParser.Parse("   ");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.Empty>(failure.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Parse_NullOrWhitespace_ReturnsEmpty(string? input)
    {
        var result = DurationParser.Parse(input!);

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.Empty>(failure.Error);
    }

    #endregion

    #region Parse - Invalid Format

    [Theory]
    [InlineData("30", "number without unit")]
    [InlineData("s", "unit without number")]
    [InlineData("30 s", "space between number and unit")]
    [InlineData("30.5s", "decimal number")]
    [InlineData("-30s", "negative number")]
    [InlineData("s30", "unit before number")]
    [InlineData("30ss", "duplicate unit")]
    [InlineData("abc", "non-numeric value")]
    [InlineData("30s30s", "multiple values")]
    public void Parse_InvalidFormat_ReturnsInvalidFormat(string input, string description)
    {
        var result = DurationParser.Parse(input);

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        var error = Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
        error.Input.Should().Be(input, $"Input '{input}' ({description}) should return InvalidFormat");
    }

    [Fact]
    public void Parse_NumberWithoutUnit_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("30");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_UnitWithoutNumber_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("s");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_SpaceBetweenNumberAndUnit_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("30 s");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_DecimalNumber_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("30.5s");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_NegativeNumber_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("-30s");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_UnitBeforeNumber_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("s30");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    #endregion

    #region Parse - Invalid Units

    [Theory]
    [InlineData("30d", "days")]
    [InlineData("30ms", "milliseconds")]
    [InlineData("1w", "weeks")]
    [InlineData("1y", "years")]
    [InlineData("30x", "unknown unit")]
    [InlineData("30sec", "long unit name")]
    [InlineData("30min", "long unit name")]
    [InlineData("30hr", "long unit name")]
    public void Parse_InvalidUnit_ReturnsInvalidFormat(string input, string description)
    {
        var result = DurationParser.Parse(input);

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        var error = Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
        error.Input.Should().Be(input, $"Input '{input}' ({description}) should return InvalidFormat");
    }

    [Fact]
    public void Parse_DaysUnit_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("30d");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_MillisecondsUnit_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("30ms");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_WeeksUnit_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("1w");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    [Fact]
    public void Parse_YearsUnit_ReturnsInvalidFormat()
    {
        var result = DurationParser.Parse("1y");

        var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
        Assert.IsType<DurationParseError.InvalidFormat>(failure.Error);
    }

    #endregion

    #region IsValid - Valid Formats

    [Theory]
    [InlineData("1s")]
    [InlineData("30s")]
    [InlineData("60s")]
    [InlineData("120s")]
    [InlineData("3600s")]
    public void IsValid_ValidSeconds_ReturnsTrue(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("1m")]
    [InlineData("5m")]
    [InlineData("30m")]
    [InlineData("60m")]
    public void IsValid_ValidMinutes_ReturnsTrue(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("1h")]
    [InlineData("2h")]
    [InlineData("24h")]
    public void IsValid_ValidHours_ReturnsTrue(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("30S")]
    [InlineData("5M")]
    [InlineData("2H")]
    public void IsValid_UppercaseUnits_ReturnsTrue(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("  30s")]
    [InlineData("30s  ")]
    [InlineData("  30s  ")]
    public void IsValid_WithWhitespace_ReturnsTrue(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeTrue();
    }

    #endregion

    #region IsValid - Invalid Formats

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_NullOrEmpty_ReturnsFalse(string? input)
    {
        var result = DurationParser.IsValid(input!);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("30")]
    [InlineData("s")]
    [InlineData("30 s")]
    [InlineData("30.5s")]
    [InlineData("-30s")]
    [InlineData("s30")]
    [InlineData("abc")]
    public void IsValid_InvalidFormat_ReturnsFalse(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("30d")]
    [InlineData("30ms")]
    [InlineData("1w")]
    [InlineData("1y")]
    [InlineData("30sec")]
    [InlineData("30min")]
    [InlineData("30hr")]
    public void IsValid_InvalidUnit_ReturnsFalse(string input)
    {
        var result = DurationParser.IsValid(input);
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValid_DoesNotThrow()
    {
        // This test verifies IsValid does not throw for any input
        var inputs = new[] { null!, "", "   ", "30", "s", "30s", "30 s", "30.5s", "-30s", "30d", "abc" };

        foreach (var input in inputs)
        {
            var action = () => DurationParser.IsValid(input);
            action.Should().NotThrow($"IsValid should not throw for input '{input ?? "null"}'");
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_ZeroSeconds_ReturnsZeroTimeSpan()
    {
        var result = DurationParser.Parse("0s");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Parse_ZeroMinutes_ReturnsZeroTimeSpan()
    {
        var result = DurationParser.Parse("0m");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Parse_ZeroHours_ReturnsZeroTimeSpan()
    {
        var result = DurationParser.Parse("0h");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Parse_LargeValue_ReturnsCorrectTimeSpan()
    {
        var result = DurationParser.Parse("999999s");

        var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
        success.Value.Should().Be(TimeSpan.FromSeconds(999999));
    }

    [Fact]
    public void IsValid_ZeroValue_ReturnsTrue()
    {
        var result = DurationParser.IsValid("0s");
        result.Should().BeTrue();
    }

    #endregion
}
