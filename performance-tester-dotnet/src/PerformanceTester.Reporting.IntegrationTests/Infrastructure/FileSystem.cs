namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// File system operations for creating and managing temporary files and directories in integration tests.
/// </summary>
public sealed class FileSystem
{
    /// <summary>
    /// Creates a temporary file path for testing. File is not created, only the path is returned.
    /// </summary>
    /// <param name="prefix">Prefix for the temp file name (e.g., "comparison-report-test")</param>
    /// <param name="extension">File extension including the dot (e.g., ".md", ".json")</param>
    /// <returns>Full path to a temporary file that doesn't exist yet</returns>
    public string CreateTempFilePath(string prefix, string extension)
    {
        var tempDir = Path.GetTempPath();
        var filename = $"{prefix}-{Guid.NewGuid():N}{extension}";
        return Path.Combine(tempDir, filename);
    }

    /// <summary>
    /// Creates a temporary directory for testing.
    /// </summary>
    /// <param name="prefix">Prefix for the temp directory name (e.g., "report-test", "PerformanceTestCharts")</param>
    /// <returns>Full path to the created temporary directory</returns>
    public string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Best-effort cleanup of a temporary file. Does not throw exceptions.
    /// </summary>
    /// <param name="path">Path to the file to delete</param>
    public void CleanupTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup - ignore failures
        }
    }

    /// <summary>
    /// Best-effort cleanup of a temporary directory. Does not throw exceptions.
    /// </summary>
    /// <param name="directory">Path to the directory to delete</param>
    public void CleanupTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup - ignore failures
        }
    }
}
