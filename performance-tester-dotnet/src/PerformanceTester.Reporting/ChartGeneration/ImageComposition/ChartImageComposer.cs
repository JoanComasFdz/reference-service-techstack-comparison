using PerformanceTester.Reporting.ChartGeneration.Configuration;
using ScottPlot;
using SkiaSharp;

namespace PerformanceTester.Reporting.ChartGeneration.ImageComposition;

/// <summary>
/// Composes multiple plots into a single vertically-stacked image.
/// </summary>
internal static class ChartImageComposer
{
    /// <summary>
    /// Renders a plot to a bitmap with the specified height.
    /// </summary>
    public static SKBitmap RenderPlotToBitmap(Plot plot, int height, PlotDimensions dimensions)
    {
        var image = plot.GetImage(dimensions.Width, height);
        return SKBitmap.Decode(image.GetImageBytes());
    }

    /// <summary>
    /// Combines multiple bitmaps vertically and saves to file.
    /// </summary>
    public static void CombineAndSave(IReadOnlyList<SKBitmap> bitmaps, string outputPath)
    {
        if (bitmaps.Count == 0)
        {
            throw new ArgumentException("No bitmaps to combine", nameof(bitmaps));
        }

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
