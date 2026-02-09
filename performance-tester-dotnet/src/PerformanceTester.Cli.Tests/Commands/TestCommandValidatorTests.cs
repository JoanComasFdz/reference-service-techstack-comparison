using PerformanceTester.Cli.Commands;

namespace PerformanceTester.Cli.Tests.Commands;

/// <summary>
/// Unit tests for TestCommand.ValidateOptions method.
/// Tests cover all validation rules for command line options.
/// </summary>
public class TestCommandValidatorTests
{
    #region Helper Methods

    private static TestCommandOptions CreateValidOptions(
        int? events = null,
        string? apiDuration = null,
        int? apiWorkers = null,
        int? port = null,
        string? database = null,
        string? resultsFolder = null,
        int? warmupEvents = null,
        int? warmupApiCalls = null,
        string? inactivityTimeout = null,
        string? rabbitMqContainer = null,
        string? postgresContainer = null)
    {
        return new TestCommandOptions(
            events ?? 10000,
            apiDuration ?? "30s",
            apiWorkers ?? 1,
            port ?? 8080,
            database ?? "testdb",
            resultsFolder ?? "./test-results",
            warmupEvents ?? 200,
            warmupApiCalls ?? 10,
            inactivityTimeout ?? "120s",
            rabbitMqContainer ?? "performancetest-rabbitmq",
            postgresContainer ?? "performancetest-postgres");
    }

    #endregion

    #region Happy Path Tests

    [Fact]
    public void ValidateOptions_WithAllValidParameters_ReturnsNull()
    {
        // Arrange
        var options = CreateValidOptions();

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_WithMinimumValidValues_ReturnsNull()
    {
        // Arrange
        var options = CreateValidOptions(
            events: 1,
            apiWorkers: 1,
            port: 1,
            warmupEvents: 0,
            warmupApiCalls: 0);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_WithMaximumValidValues_ReturnsNull()
    {
        // Arrange
        var options = CreateValidOptions(
            events: 1000000,
            apiWorkers: 1000,
            port: 65535,
            warmupEvents: 10000,
            warmupApiCalls: 1000);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ResultsFolder Validation Tests

    [Theory]
    [InlineData("./test-results")]
    [InlineData(".")]
    [InlineData("/tmp/results")]
    [InlineData("results")]
    [InlineData("path/to/results")]
    public void ValidateOptions_WithValidResultsFolder_ReturnsNull(string resultsFolder)
    {
        // Arrange
        var options = CreateValidOptions(resultsFolder: resultsFolder);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateOptions_WithInvalidResultsFolder_ReturnsError(string resultsFolder)
    {
        // Arrange
        var options = CreateValidOptions(resultsFolder: resultsFolder);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Results folder cannot be empty");
    }

    #endregion

    #region RabbitMqContainer Validation Tests

    [Theory]
    [InlineData("rabbitmq")]
    [InlineData("my-rabbit")]
    [InlineData("performancetest-rabbitmq")]
    [InlineData("rabbit_container_1")]
    public void ValidateOptions_WithValidRabbitMqContainer_ReturnsNull(string rabbitMqContainer)
    {
        // Arrange
        var options = CreateValidOptions(rabbitMqContainer: rabbitMqContainer);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateOptions_WithInvalidRabbitMqContainer_ReturnsError(string rabbitMqContainer)
    {
        // Arrange
        var options = CreateValidOptions(rabbitMqContainer: rabbitMqContainer);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("RabbitMQ container name cannot be empty");
    }

    #endregion

    #region PostgresContainer Validation Tests

    [Theory]
    [InlineData("postgres")]
    [InlineData("my-pg")]
    [InlineData("performancetest-postgres")]
    [InlineData("pg_container_1")]
    public void ValidateOptions_WithValidPostgresContainer_ReturnsNull(string postgresContainer)
    {
        // Arrange
        var options = CreateValidOptions(postgresContainer: postgresContainer);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateOptions_WithInvalidPostgresContainer_ReturnsError(string postgresContainer)
    {
        // Arrange
        var options = CreateValidOptions(postgresContainer: postgresContainer);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("PostgreSQL container name cannot be empty");
    }

    #endregion

    #region Validation Order Tests

    [Fact]
    public void ValidateOptions_WithMultipleInvalidParameters_ReturnsFirstError()
    {
        // Arrange - ResultsFolder is validated first (Events, ApiWorkers, ApiDuration, Port, WarmupEvents, WarmupApiCalls, Database, InactivityTimeout are now value objects)
        var options = new TestCommandOptions(
            Events: 10000,
            ApiDuration: "30s",
            ApiWorkers: 1,
            Port: 8080,
            Database: "testdb",
            ResultsFolder: "",      // Invalid - first check in ValidateOptions
            WarmupEvents: 200,
            WarmupApiCalls: 10,
            InactivityTimeout: "120s",
            RabbitMqContainer: "",
            PostgresContainer: ""
        );

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert - Should return results folder error (validated first)
        result.Should().NotBeNull();
        result.Should().Contain("Results folder cannot be empty");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ValidateOptions_WithBoundaryValues_ReturnsNull()
    {
        // Arrange - Test all boundary values at once
        var options = new TestCommandOptions(
            Events: 1,              // Minimum
            ApiDuration: "1s",      // Minimum duration
            ApiWorkers: 1,          // Minimum
            Port: 1,                // Minimum
            Database: "a",          // Single character
            ResultsFolder: ".",     // Single character
            WarmupEvents: 0,        // Minimum (can be zero)
            WarmupApiCalls: 0,      // Minimum (can be zero)
            InactivityTimeout: "1s", // Minimum duration
            RabbitMqContainer: "a", // Single character
            PostgresContainer: "a"  // Single character
        );

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_WithLargeDurationValues_ReturnsNull()
    {
        // Arrange
        var options = CreateValidOptions(
            inactivityTimeout: "9999m" // Very large but valid
        );

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
