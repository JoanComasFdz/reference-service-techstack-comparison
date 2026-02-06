using System.Text.Json;

namespace PerformanceTester.Cli.Toolbox;

/// <summary>
/// Reusable JSON file loading utilities.
/// </summary>
internal static class JsonFileToolbox
{
    /// <summary>
    /// Loads samples from a JSON file containing a "samples" array, plus optional root-level metadata.
    /// Returns empty list if file doesn't exist or parsing fails.
    /// </summary>
    public static async Task<IReadOnlyList<T>> LoadSamplesAsync<T>(
        string filePath,
        Func<JsonElement, JsonElement, T> mapSample,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return Array.Empty<T>();

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("samples", out var samplesElement))
                return Array.Empty<T>();

            var samples = new List<T>();
            foreach (var sample in samplesElement.EnumerateArray())
            {
                samples.Add(mapSample(sample, root));
            }

            return samples;
        }
        catch
        {
            return Array.Empty<T>();
        }
    }
}
