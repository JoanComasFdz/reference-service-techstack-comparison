using System.Runtime.CompilerServices;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Extension methods for StreamReader.
/// </summary>
internal static class StreamReaderExtensions
{
    /// <summary>
    /// Reads lines from StreamReader as async enumerable.
    /// </summary>
    /// <param name="reader">The StreamReader to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of lines.</returns>
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        this StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            yield return line;
        }
    }
}
