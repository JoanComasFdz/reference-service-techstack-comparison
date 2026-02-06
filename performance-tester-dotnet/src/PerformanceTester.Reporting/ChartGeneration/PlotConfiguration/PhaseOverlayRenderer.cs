using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration.PlotConfiguration;

/// <summary>
/// Renders phase boundaries and labels on plots.
/// </summary>
internal static class PhaseOverlayRenderer
{
    /// <summary>
    /// Adds phase boundary vertical lines to a plot.
    /// </summary>
    public static void AddPhaseBoundaries(Plot plot, TestReport testReport, ChartConfig config)
    {
        var phase1Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase1Start);
        var phase2End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase2End);
        var phase3Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3Start);
        var phase3End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3End);

        var boundaries = new[]
        {
            (Time: phase1Start, Color: ChartColors.ConsumePhase),
            (Time: phase2End, Color: ChartColors.ConsumeBoundary),
            (Time: phase3Start, Color: ChartColors.ApiPhase),
            (Time: phase3End, Color: ChartColors.ApiPhase)
        };

        foreach (var (time, color) in boundaries)
        {
            var oaDate = time.ToOADate();
            var vline = plot.Add.VerticalLine(oaDate);
            vline.Color = color.WithAlpha(0.6);
            vline.LinePattern = LinePattern.Dotted;
            vline.LineWidth = config.Line.BoundaryLineWidth;
        }
    }

    /// <summary>
    /// Adds phase labels to the top plot (throughput plot).
    /// </summary>
    public static void AddPhaseLabels(Plot plot, TestReport testReport, double yMax, ChartConfig config)
    {
        var phase1Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase1Start);
        var phase2End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase2End);
        var phase3Start = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3Start);
        var phase3End = testReport.TestDate.AddSeconds(testReport.PhaseTimestamps.Phase3End);

        var labelYPosition = yMax * 0.95;

        AddConsumeLabel(plot, phase1Start, phase2End, testReport.Configuration.NumEvents, labelYPosition, config);
        AddApiLabel(plot, phase3Start, phase3End, testReport.Configuration, labelYPosition, config);
    }

    /// <summary>
    /// Adds title to the top plot.
    /// </summary>
    public static void AddTitle(Plot plot, TestReport testReport, ChartConfig config)
    {
        var humanDate = testReport.TestDate.ToString("MMMM dd, yyyy");
        var humanTime = testReport.TestDate.ToString("HH:mm:ss");
        var title = $"Performance Metrics - {testReport.MonitoredProcess.Name} - {humanDate} at {humanTime}";

        plot.Axes.Title.Label.Text = title;
        plot.Axes.Title.Label.FontSize = config.Font.TitleFontSize;
        plot.Axes.Title.Label.FontName = config.Font.FontName;
        plot.Axes.Title.Label.Bold = true;
    }

    private static void AddConsumeLabel(Plot plot, DateTime phase1Start, DateTime phase2End, int numEvents, double yPosition, ChartConfig config)
    {
        var midTime = phase1Start.AddTicks((phase2End - phase1Start).Ticks / 2);
        var label = numEvents > 0 ? $"Consume: {numEvents}" : "Consume";

        var text = plot.Add.Text(label, midTime.ToOADate(), yPosition);
        text.LabelFontColor = ChartColors.ConsumePhase;
        text.LabelFontSize = config.Font.PhaseLabelFontSize;
        text.LabelFontName = config.Font.FontName;
        text.LabelBold = true;
        text.LabelAlignment = Alignment.UpperCenter;
    }

    private static void AddApiLabel(Plot plot, DateTime phase3Start, DateTime phase3End, TestConfiguration config, double yPosition, ChartConfig chartConfig)
    {
        var midTime = phase3Start.AddTicks((phase3End - phase3Start).Ticks / 2);
        var label = "API";

        if (!string.IsNullOrEmpty(config.ApiDuration))
        {
            label = config.ApiConcurrentWorkers > 0
                ? $"API: {config.ApiDuration} ({config.ApiConcurrentWorkers}w)"
                : $"API: {config.ApiDuration}";
        }

        var text = plot.Add.Text(label, midTime.ToOADate(), yPosition);
        text.LabelFontColor = ChartColors.ApiPhase;
        text.LabelFontSize = chartConfig.Font.PhaseLabelFontSize;
        text.LabelFontName = chartConfig.Font.FontName;
        text.LabelBold = true;
        text.LabelAlignment = Alignment.UpperCenter;
    }
}
