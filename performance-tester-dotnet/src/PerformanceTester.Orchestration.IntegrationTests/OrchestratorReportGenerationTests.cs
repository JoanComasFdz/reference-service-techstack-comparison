using System.Text.Json;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests;

/// <summary>
/// Tests for report generation functionality of the orchestrator.
/// Verifies that test reports are created correctly with valid content.
/// </summary>
public sealed class OrchestratorReportGenerationTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Test 4.2: Verifies that RunTestAsync creates all expected report files.
    /// Expected files: 1 chart PNG + at least 7 JSON files = at least 8 files total.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_ShouldCreateAllExpectedReportFiles()
    {
        // Arrange
        const int testPort = 9950;
        const int eventCount = 100;
        const int warmupEventCount = 50;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        // Create unique temporary results folder
        var resultsFolder = Path.Combine(Path.GetTempPath(), $"test-results-{Guid.NewGuid()}");

        try
        {
            var config = new TestConfigurationBuilder()
                .WithEventCount(eventCount)
                .WithWarmupEventCount(warmupEventCount)
                .WithApiWorkers(1)
                .WithApiDuration(TimeSpan.FromSeconds(5))
                .WithServicePort(testPort)
                .WithResultsFolder(resultsFolder)
                .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
                .Build();

            // Act
            var result = await System.Orchestration.RunTestAsync(config);
            Assert.True(result.IsSuccess, $"Expected success but got failure: {(result.IsFailure ? result.FailureError.Message : "")}");
            var report = result.SuccessValue;

            // Assert
            Assert.True(Directory.Exists(resultsFolder),
                $"Results folder should exist: {resultsFolder}");

            var allFiles = Directory.GetFiles(resultsFolder);
            Output.WriteLine($"Found {allFiles.Length} files in results folder:");
            foreach (var file in allFiles)
            {
                Output.WriteLine($"  - {Path.GetFileName(file)}");
            }

            // Should have at least 8 files total
            Assert.True(
                allFiles.Length >= 8,
                $"Expected at least 8 report files, found {allFiles.Length}");

            // Should have exactly 1 chart PNG file
            var chartFiles = allFiles.Where(f => f.EndsWith(".chart.png")).ToArray();
            Assert.Single(chartFiles);
            Output.WriteLine($"Chart file: {Path.GetFileName(chartFiles[0])}");

            // Should have at least 7 JSON files
            var jsonFiles = allFiles.Where(f => f.EndsWith(".json")).ToArray();
            Assert.True(
                jsonFiles.Length >= 7,
                $"Expected at least 7 JSON files, found {jsonFiles.Length}");
            Output.WriteLine($"JSON files count: {jsonFiles.Length}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);

            // Clean up temp folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
                Output.WriteLine($"Cleaned up results folder: {resultsFolder}");
            }
        }
    }

    /// <summary>
    /// Test 4.3: Verifies that RunTestAsync throws a clear error when results folder is invalid.
    /// Invalid paths (non-existent readonly paths) should result in IOException,
    /// UnauthorizedAccessException, or DirectoryNotFoundException.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenResultsFolderInvalid_ShouldThrowClearError()
    {
        // Arrange
        const int testPort = 9951;
        const int eventCount = 100;
        const int warmupEventCount = 50;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        try
        {
            // Use an invalid path that should fail on any OS
            var invalidResultsFolder = "/nonexistent/readonly/path/results";

            var config = new TestConfigurationBuilder()
                .WithEventCount(eventCount)
                .WithWarmupEventCount(warmupEventCount)
                .WithApiWorkers(1)
                .WithApiDuration(TimeSpan.FromSeconds(5))
                .WithServicePort(testPort)
                .WithResultsFolder(invalidResultsFolder)
                .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
                .Build();

            // Act & Assert
            var result = await System.Orchestration.RunTestAsync(config);

            Assert.True(result.IsFailure, "Expected a failure result for invalid results folder");

            // Verify the failure message contains filesystem-related information
            var isFileSystemRelated = result.FailureError.Message.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                                      result.FailureError.Message.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                                      result.FailureError.Message.Contains("directory", StringComparison.OrdinalIgnoreCase) ||
                                      result.FailureError.Message.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                                      result.FailureError.Message.Contains("denied", StringComparison.OrdinalIgnoreCase);

            Assert.True(isFileSystemRelated,
                $"Expected a filesystem-related failure message. Got: {result.FailureError.Message}");

            Output.WriteLine($"Got expected failure: {result.FailureError.Phase} - {result.FailureError.Message}");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 4.4: Verifies that RunTestAsync creates results folder if it doesn't exist.
    /// This includes nested directory structures.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_WhenResultsFolderNotExists_ShouldCreateIt()
    {
        // Arrange
        const int testPort = 9952;
        const int eventCount = 100;
        const int warmupEventCount = 50;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        // Create path with nested non-existent directories
        var nestedResultsFolder = Path.Combine(
            Path.GetTempPath(),
            $"nested-{Guid.NewGuid()}",
            "deep",
            $"test-results-{Guid.NewGuid()}");

        // Get the root of the nested path for cleanup
        var rootNestedFolder = Path.Combine(
            Path.GetTempPath(),
            nestedResultsFolder.Split(Path.DirectorySeparatorChar)
                .SkipWhile(p => !p.StartsWith("nested-"))
                .First());

        try
        {
            // Verify folder does NOT exist before test
            Assert.False(Directory.Exists(nestedResultsFolder),
                $"Results folder should NOT exist before test: {nestedResultsFolder}");
            Output.WriteLine($"Confirmed results folder does not exist: {nestedResultsFolder}");

            var config = new TestConfigurationBuilder()
                .WithEventCount(eventCount)
                .WithWarmupEventCount(warmupEventCount)
                .WithApiWorkers(1)
                .WithApiDuration(TimeSpan.FromSeconds(5))
                .WithServicePort(testPort)
                .WithResultsFolder(nestedResultsFolder)
                .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
                .Build();

            // Act
            var result = await System.Orchestration.RunTestAsync(config);
            Assert.True(result.IsSuccess, $"Expected success but got failure: {(result.IsFailure ? result.FailureError.Message : "")}");
            var report = result.SuccessValue;

            // Assert - folder IS created after test
            Assert.True(Directory.Exists(nestedResultsFolder),
                $"Results folder should be created after test: {nestedResultsFolder}");
            Output.WriteLine($"Results folder was created: {nestedResultsFolder}");

            // Assert - folder contains files
            var files = Directory.GetFiles(nestedResultsFolder);
            Assert.NotEmpty(files);
            Output.WriteLine($"Results folder contains {files.Length} files");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);

            // Clean up nested directories - delete from root
            if (Directory.Exists(rootNestedFolder))
            {
                Directory.Delete(rootNestedFolder, recursive: true);
                Output.WriteLine($"Cleaned up nested folder: {rootNestedFolder}");
            }
        }
    }

    /// <summary>
    /// Test 4.5: Verifies that all generated JSON report files contain valid JSON.
    /// Each .json file should be parseable by JsonDocument.Parse() without exceptions.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_GeneratedReports_ShouldBeValidJson()
    {
        // Arrange
        const int testPort = 9953;
        const int eventCount = 100;
        const int warmupEventCount = 50;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        // Create unique temporary results folder
        var resultsFolder = Path.Combine(Path.GetTempPath(), $"test-results-{Guid.NewGuid()}");

        try
        {
            var config = new TestConfigurationBuilder()
                .WithEventCount(eventCount)
                .WithWarmupEventCount(warmupEventCount)
                .WithApiWorkers(1)
                .WithApiDuration(TimeSpan.FromSeconds(5))
                .WithServicePort(testPort)
                .WithResultsFolder(resultsFolder)
                .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
                .Build();

            // Act
            var result = await System.Orchestration.RunTestAsync(config);
            Assert.True(result.IsSuccess, $"Expected success but got failure: {(result.IsFailure ? result.FailureError.Message : "")}");
            var report = result.SuccessValue;

            // Assert - get all JSON files
            var jsonFiles = Directory.GetFiles(resultsFolder, "*.json");
            Assert.NotEmpty(jsonFiles);
            Output.WriteLine($"Found {jsonFiles.Length} JSON files to validate:");

            // Validate each JSON file
            var validationErrors = new List<string>();
            foreach (var jsonFile in jsonFiles)
            {
                var fileName = Path.GetFileName(jsonFile);
                try
                {
                    var content = await File.ReadAllTextAsync(jsonFile);

                    // Parse with JsonDocument - will throw if invalid JSON
                    using var document = JsonDocument.Parse(content);

                    // Verify it has a valid root element (not Undefined)
                    Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
                    Output.WriteLine($"  Valid: {fileName} (root element type: {document.RootElement.ValueKind})");
                }
                catch (JsonException ex)
                {
                    validationErrors.Add($"{fileName}: {ex.Message}");
                    Output.WriteLine($"  INVALID: {fileName} - {ex.Message}");
                }
            }

            // Assert no validation errors
            Assert.Empty(validationErrors);
            Output.WriteLine($"All {jsonFiles.Length} JSON files are valid");
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);

            // Clean up temp folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
                Output.WriteLine($"Cleaned up results folder: {resultsFolder}");
            }
        }
    }
}
