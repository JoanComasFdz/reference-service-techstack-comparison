using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;
using SkiaSharp;

namespace PerformanceTester.Reporting.ChartGeneration.ImageComposition;

/// <summary>
/// Composes multiple plots into a single vertically-stacked image.
/// </summary>
internal sealed class ChartImageComposer
{
    private readonly PlotDimensions _dimensions;

    public ChartImageComposer(PlotDimensions dimensions)
    {
        _dimensions = dimensions;
    }

    /// <summary>
    /// Renders a plot to a bitmap with the specified height.
    /// </summary>
    public SKBitmap RenderPlotToBitmap(Plot plot, int height)
    {
        var image = plot.GetImage(_dimensions.Width, height);
        return SKBitmap.Decode(image.GetImageBytes());
    }

    /// <summary>
    /// Combines multiple bitmaps vertically and saves to file.
    /// </summary>
    public void CombineAndSave(IReadOnlyList<SKBitmap> bitmaps, string outputPath)
    {
        if (bitmaps.Count == 0)
            throw new ArgumentException("No bitmaps to combine", nameof(bitmaps));

        var totalHeight = bitmaps.Sum(b => b.Height);
        var width = bitmaps[0].Width;

        using var combinedBitmap = new SKBitmap(width, totalHeight);
        using var canvas = new SKCanvas(combinedBitmap);

        canvas.Clear(SKColors.White);

        int yOffset = 0;
        foreach (var bitmap in bitmaps)
        {
            canvas.DrawBitmap(bitmap, 0, yOffset);
            yOffset += bitmap.Height;
        }

        using var fileStream = File.OpenWrite(outputPath);
        combinedBitmap.Encode(fileStream, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// Disposes all bitmaps in the list.
    /// </summary>
    public static void DisposeBitmaps(IReadOnlyList<SKBitmap> bitmaps)
    {
        foreach (var bitmap in bitmaps)
        {
            bitmap.Dispose();
        }
    }
}
