using Dunet;

namespace PerformanceTester.Cli.Configuration;

/// <summary>
/// Represents the reasons why a duration string could not be parsed.
/// </summary>
[Union]
public partial record DurationParseError
{
    /// <summary>Input was null, empty, or whitespace.</summary>
    public partial record Empty;

    /// <summary>Input did not match the expected format &lt;number&gt;&lt;unit&gt;.</summary>
    public partial record InvalidFormat(string Input);

    /// <summary>The unit character was not a recognized duration unit (s, m, h).</summary>
    public partial record UnknownUnit(char Unit);
}
