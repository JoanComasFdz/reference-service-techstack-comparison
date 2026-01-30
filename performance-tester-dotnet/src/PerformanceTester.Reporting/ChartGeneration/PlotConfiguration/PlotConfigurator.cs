using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;

/// <summary>
/// Configures common plot elements (legend, grid, time axis, layout).
/// Used by all plot builders to ensure consistent styling.
/// </summary>
internal static class PlotConfigurator
{
    /// <summary>
    /// Configures the time axis to display HH:mm:ss format.
    /// </summary>
    public static void ConfigureTimeAxis(Plot plot)
    {
        var tickGen = new ScottPlot.TickGenerators.DateTimeAutomatic
        {
            LabelFormatter = dt => dt.ToString("HH:mm:ss")
        };
        plot.Axes.Bottom.TickGenerator = tickGen;
    }

    /// <summary>
    /// Configures the grid with standard styling.
    /// </summary>
    public static void ConfigureGrid(Plot plot, GridConfig config)
    {
        plot.Grid.MajorLineColor = Color.FromHex(config.LineColorHex).WithAlpha(config.LineAlpha);
    }

    /// <summary>
    /// Configures the legend on the right edge of the plot.
    /// </summary>
    public static void ConfigureLegend(Plot plot, FontConfig fontConfig)
    {
        plot.ShowLegend(Edge.Right);
        plot.Legend.OutlineColor = Colors.Transparent;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.FontSize = fontConfig.LegendFontSize;
        plot.Legend.FontName = fontConfig.FontName;
        plot.Legend.Orientation = Orientation.Vertical;
        plot.Legend.InterItemPadding = new PixelPadding(0, 0, 15, 0);
    }

    /// <summary>
    /// Configures the plot layout with standard padding.
    /// </summary>
    public static void ConfigureLayout(Plot plot, PlotDimensions dimensions, bool hasTitle = false)
    {
        var topPadding = hasTitle ? dimensions.PaddingTopWithTitle : dimensions.PaddingTop;
        plot.Layout.Fixed(new PixelPadding(
            left: dimensions.PaddingLeft,
            right: dimensions.PaddingRight,
            bottom: dimensions.PaddingBottom,
            top: topPadding));
    }

    /// <summary>
    /// Configures a Y-axis label with standard styling.
    /// </summary>
    public static void ConfigureAxisLabel(
        IAxis axis,
        string text,
        Color color,
        FontConfig fontConfig)
    {
        axis.Label.Text = text;
        axis.Label.ForeColor = color;
        axis.Label.Bold = true;
        axis.Label.FontSize = fontConfig.AxisLabelFontSize;
        axis.Label.FontName = fontConfig.FontName;
    }

    /// <summary>
    /// Configures the bottom axis label (X-axis) with "Time" text.
    /// </summary>
    public static void ConfigureBottomAxisLabel(Plot plot, FontConfig fontConfig)
    {
        plot.Axes.Bottom.Label.Text = "Time";
        plot.Axes.Bottom.Label.Bold = true;
        plot.Axes.Bottom.Label.FontSize = fontConfig.AxisLabelFontSize;
        plot.Axes.Bottom.Label.FontName = fontConfig.FontName;
    }

    /// <summary>
    /// Configures tick label rotation for bottom axis.
    /// </summary>
    public static void ConfigureTickLabelRotation(Plot plot)
    {
        plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleLeft;
    }

    /// <summary>
    /// Applies all standard configuration to a plot.
    /// </summary>
    public static void ApplyStandardConfiguration(Plot plot, ChartConfig config, bool hasTitle = false)
    {
        ConfigureGrid(plot, config.Grid);
        ConfigureLegend(plot, config.Font);
        ConfigureLayout(plot, config.Dimensions, hasTitle);
        ConfigureTimeAxis(plot);
    }
}
