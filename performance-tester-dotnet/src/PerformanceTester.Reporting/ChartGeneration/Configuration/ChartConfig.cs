namespace PerformanceTester.Reporting.ChartGeneration.Configuration;

/// <summary>
/// Font configuration for chart elements.
/// </summary>
public sealed record FontConfig
{
    /// <summary>
    /// Default chart font (DejaVu Sans for cross-platform consistency).
    /// </summary>
    public static readonly FontConfig Default = new();

    /// <summary>
    /// Font family name.
    /// </summary>
    public string FontName { get; init; } = "DejaVu Sans";

    /// <summary>
    /// Title font size in pixels.
    /// </summary>
    public float TitleFontSize { get; init; } = 36f;

    /// <summary>
    /// Axis label font size in pixels.
    /// </summary>
    public float AxisLabelFontSize { get; init; } = 26f;

    /// <summary>
    /// Legend font size in pixels.
    /// </summary>
    public float LegendFontSize { get; init; } = 22f;

    /// <summary>
    /// Phase label font size in pixels.
    /// </summary>
    public float PhaseLabelFontSize { get; init; } = 22f;
}

/// <summary>
/// Line styling configuration.
/// </summary>
public sealed record LineConfig
{
    /// <summary>
    /// Default line configuration.
    /// </summary>
    public static readonly LineConfig Default = new();

    /// <summary>
    /// Primary data line width.
    /// </summary>
    public float PrimaryLineWidth { get; init; } = 4f;

    /// <summary>
    /// Average/reference line width.
    /// </summary>
    public float AverageLineWidth { get; init; } = 3f;

    /// <summary>
    /// Phase boundary line width.
    /// </summary>
    public float BoundaryLineWidth { get; init; } = 3f;

    /// <summary>
    /// Fill alpha for primary lines (0-1).
    /// </summary>
    public double PrimaryFillAlpha { get; init; } = 0.3;

    /// <summary>
    /// Fill alpha for secondary lines like RAM (0-1).
    /// </summary>
    public double SecondaryFillAlpha { get; init; } = 0.2;
}

/// <summary>
/// Plot dimension configuration.
/// </summary>
public sealed record PlotDimensions
{
    /// <summary>
    /// Default plot dimensions.
    /// </summary>
    public static readonly PlotDimensions Default = new();

    /// <summary>
    /// Plot width in pixels.
    /// </summary>
    public int Width { get; init; } = 2400;

    /// <summary>
    /// Standard subplot height in pixels.
    /// </summary>
    public int Height { get; init; } = 480;

    /// <summary>
    /// Throughput plot height (taller for title and 2 legend items).
    /// </summary>
    public int ThroughputHeight { get; init; } = 630;

    /// <summary>
    /// Left padding in pixels.
    /// </summary>
    public int PaddingLeft { get; init; } = 100;

    /// <summary>
    /// Right padding in pixels (space for legend).
    /// </summary>
    public int PaddingRight { get; init; } = 480;

    /// <summary>
    /// Bottom padding in pixels.
    /// </summary>
    public int PaddingBottom { get; init; } = 50;

    /// <summary>
    /// Top padding for standard plots.
    /// </summary>
    public int PaddingTop { get; init; } = 50;

    /// <summary>
    /// Top padding for title plot (extra space for title).
    /// </summary>
    public int PaddingTopWithTitle { get; init; } = 100;
}

/// <summary>
/// Grid configuration.
/// </summary>
public sealed record GridConfig
{
    /// <summary>
    /// Default grid configuration.
    /// </summary>
    public static readonly GridConfig Default = new();

    /// <summary>
    /// Grid line color hex code.
    /// </summary>
    public string LineColorHex { get; init; } = "#cccccc";

    /// <summary>
    /// Grid line alpha (0-1).
    /// </summary>
    public double LineAlpha { get; init; } = 0.3;
}

/// <summary>
/// Combined chart configuration.
/// </summary>
public sealed record ChartConfig
{
    /// <summary>
    /// Default chart configuration.
    /// </summary>
    public static readonly ChartConfig Default = new();

    /// <summary>
    /// Font settings.
    /// </summary>
    public FontConfig Font { get; init; } = FontConfig.Default;

    /// <summary>
    /// Line styling settings.
    /// </summary>
    public LineConfig Line { get; init; } = LineConfig.Default;

    /// <summary>
    /// Plot dimension settings.
    /// </summary>
    public PlotDimensions Dimensions { get; init; } = PlotDimensions.Default;

    /// <summary>
    /// Grid settings.
    /// </summary>
    public GridConfig Grid { get; init; } = GridConfig.Default;
}
