using Dunet;

namespace PerformanceTester.Common;

/// <summary>
/// Supported operating system platforms.
/// Dunet union provides exhaustive <see cref="Match"/> — missing cases are compile-time errors.
/// </summary>
[Union]
public partial record SupportedPlatform
{
    /// <summary>Windows operating system.</summary>
    public partial record Windows;

    /// <summary>Linux operating system.</summary>
    public partial record Linux;
}
