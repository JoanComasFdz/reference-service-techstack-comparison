namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// Extension methods for markdown string manipulation and analysis.
/// </summary>
public static class MarkdownExtensions
{
    /// <summary>
    /// Extracts a specific section from markdown text starting from the specified section header.
    /// Returns the section content up to the next section header (##) or end of document.
    /// </summary>
    /// <param name="markdown">The markdown text to search.</param>
    /// <param name="sectionHeader">The section header to find (e.g., "## Test Results").</param>
    /// <returns>The extracted section content, or empty string if section not found.</returns>
    public static string ExtractSection(this string markdown, string sectionHeader)
    {
        var lines = markdown.Split('\n');
        var startIndex = Array.FindIndex(lines, l => l.Contains(sectionHeader));

        if (startIndex == -1)
        {
            return string.Empty;
        }

        // Find next section header (starts with ##) or end of document
        var endIndex = Array.FindIndex(lines, startIndex + 1, l => l.StartsWith("##"));
        if (endIndex == -1)
        {
            endIndex = lines.Length;
        }

        var sectionLines = lines[startIndex..endIndex];
        return string.Join('\n', sectionLines);
    }

    /// <summary>
    /// Counts the number of occurrences of a substring within a text.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="substring">The substring to count.</param>
    /// <returns>The number of occurrences found.</returns>
    public static int CountOccurrences(this string text, string substring)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
}
