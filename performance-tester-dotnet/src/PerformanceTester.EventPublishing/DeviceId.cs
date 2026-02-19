using System.Text.RegularExpressions;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Value object representing a device identifier (e.g., DEVICE-001).
/// Inherits non-empty validation and trimming from <see cref="NonEmptyString"/>;
/// adds DEVICE-NNN format enforcement.
/// </summary>
internal sealed record DeviceId : NonEmptyString
{
    private static readonly Regex DeviceIdPattern = new(@"^DEVICE-\d+$", RegexOptions.Compiled);

    private DeviceId(string value) : base(value) { }

    /// <summary>
    /// Creates a DeviceId from a raw string, validating DEVICE-NNN format.
    /// </summary>
    public static Result<DeviceId, string> Create(string value)
    {
        // Leverage base NonEmptyString validation (null/whitespace check + trim)
        var baseResult = Create<DeviceId>(value, "Device ID", v => new DeviceId(v));
        if (baseResult.IsFailure)
        {
            return baseResult;
        }

        return DeviceIdPattern.IsMatch(baseResult.SuccessValue.Value)
            ? baseResult
            : new Result<DeviceId, string>.Failure(
                $"Device ID must match DEVICE-NNN format (got: '{value}')");
    }

    /// <summary>
    /// Creates a DeviceId from a 1-based index (e.g., 1 → DEVICE-001).
    /// </summary>
    public static DeviceId FromIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        return new DeviceId($"DEVICE-{index:D3}");
    }

    /// <summary>
    /// Creates a DeviceId from a string, validating DEVICE-NNN format.
    /// Throws <see cref="ArgumentException"/> on invalid input.
    /// </summary>
    public static DeviceId FromString(string value) => Create(value).Match(
        success => success.Value,
        failure => throw new ArgumentException(failure.Error, nameof(value)));
}
