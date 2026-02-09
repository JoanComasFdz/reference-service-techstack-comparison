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

    #region WarmupEvents Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(10000)]
    public void ValidateOptions_WithValidWarmupEvents_ReturnsNull(int warmupEvents)
    {
        // Arrange
        var options = CreateValidOptions(warmupEvents: warmupEvents);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidateOptions_WithWarmupEventsLessThanMinimum_ReturnsError(int warmupEvents)
    {
        // Arrange
        var options = CreateValidOptions(warmupEvents: warmupEvents);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Warmup events must be between 0 and 10000");
        result.Should().Contain($"got: {warmupEvents}");
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(20000)]
    public void ValidateOptions_WithWarmupEventsGreaterThanMaximum_ReturnsError(int warmupEvents)
    {
        // Arrange
        var options = CreateValidOptions(warmupEvents: warmupEvents);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Warmup events must be between 0 and 10000");
        result.Should().Contain($"got: {warmupEvents}");
    }

    #endregion

    #region WarmupApiCalls Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(1000)]
    public void ValidateOptions_WithValidWarmupApiCalls_ReturnsNull(int warmupApiCalls)
    {
        // Arrange
        var options = CreateValidOptions(warmupApiCalls: warmupApiCalls);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidateOptions_WithWarmupApiCallsLessThanMinimum_ReturnsError(int warmupApiCalls)
    {
        // Arrange
        var options = CreateValidOptions(warmupApiCalls: warmupApiCalls);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Warmup API calls must be between 0 and 1000");
        result.Should().Contain($"got: {warmupApiCalls}");
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(2000)]
    public void ValidateOptions_WithWarmupApiCallsGreaterThanMaximum_ReturnsError(int warmupApiCalls)
    {
        // Arrange
        var options = CreateValidOptions(warmupApiCalls: warmupApiCalls);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Warmup API calls must be between 0 and 1000");
        result.Should().Contain($"got: {warmupApiCalls}");
    }

    #endregion

    #region Database Validation Tests

    [Theory]
    [InlineData("go_db")]
    [InlineData("test-db")]
    [InlineData("my_database_123")]
    [InlineData("a")]
    public void ValidateOptions_WithValidDatabase_ReturnsNull(string database)
    {
        // Arrange
        var options = CreateValidOptions(database: database);

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
    public void ValidateOptions_WithInvalidDatabase_ReturnsError(string database)
    {
        // Arrange
        var options = CreateValidOptions(database: database);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Database name cannot be empty");
    }

    #endregion

    #region InactivityTimeout Validation Tests

    [Theory]
    [InlineData("1s")]
    [InlineData("120s")]
    [InlineData("2m")]
    [InlineData("1h")]
    [InlineData("30s")]
    public void ValidateOptions_WithValidInactivityTimeout_ReturnsNull(string inactivityTimeout)
    {
        // Arrange
        var options = CreateValidOptions(inactivityTimeout: inactivityTimeout);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("120")]
    [InlineData("2d")]
    [InlineData("timeout")]
    [InlineData("s120")]
    [InlineData("120 s")]
    [InlineData("-120s")]
    public void ValidateOptions_WithInvalidInactivityTimeout_ReturnsError(string inactivityTimeout)
    {
        // Arrange
        var options = CreateValidOptions(inactivityTimeout: inactivityTimeout);

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Invalid inactivity timeout format");
        result.Should().Contain("Expected format: <number><unit>");
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
        // Arrange - WarmupEvents is validated first (Events, ApiWorkers, ApiDuration, Port are now value objects)
        var options = new TestCommandOptions(
            Events: 10000,
            ApiDuration: "30s",
            ApiWorkers: 1,
            Port: 8080,
            Database: "",           // Invalid - checked later
            ResultsFolder: "",
            WarmupEvents: -1,       // Invalid - first check in ValidateOptions
            WarmupApiCalls: -1,
            InactivityTimeout: "",
            RabbitMqContainer: "",
            PostgresContainer: ""
        );

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert - Should return warmup events error (validated first)
        result.Should().NotBeNull();
        result.Should().Contain("Warmup events must be between 0 and 10000");
    }

    [Fact]
    public void ValidateOptions_WithInvalidWarmupEventsButPrecedingFieldsValid_ReturnsWarmupEventsError()
    {
        // Arrange - WarmupEvents is validated first in ValidateOptions
        var options = CreateValidOptions(
            port: 8080,             // Valid
            warmupEvents: -1,       // Invalid
            database: ""            // Invalid - checked later
        );

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert - Should return warmup events error
        result.Should().NotBeNull();
        result.Should().Contain("Warmup events must be between 0 and 10000");
    }

    [Fact]
    public void ValidateOptions_WithInvalidDatabaseButPrecedingFieldsValid_ReturnsDatabaseError()
    {
        // Arrange - Database is validated after WarmupApiCalls
        var options = CreateValidOptions(
            port: 8080,             // Valid
            warmupEvents: 100,      // Valid
            warmupApiCalls: 10,     // Valid
            database: ""            // Invalid
        );

        // Act
        var result = TestCommand.ValidateOptions(options);

        // Assert - Should return database error
        result.Should().NotBeNull();
        result.Should().Contain("Database name cannot be empty");
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
