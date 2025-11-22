namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Fluent builder for creating TestConfiguration instances.
/// Provides sensible defaults for all parameters.
/// Tests only specify the parameters they need to override.
/// </summary>
public class TestConfigurationBuilder
{
    private int _eventCount = 100;
    private TimeSpan _apiDuration = TimeSpan.FromSeconds(10);
    private int _apiWorkers = 1;
    private int _servicePort = 8093;
    private string _databaseName = OrchestrationSystem.IntegrationTestDatabaseName;
    private string _resultsFolder = "./test-results";
    private string _rabbitMqContainerName = "performance-tester-rabbitmq";
    private string _postgresContainerName = "performance-tester-postgres";
    private TimeSpan? _inactivityTimeout = null;
    private int _warmupEventCount = 50;
    private TimeSpan? _warmupApiDuration = null;
    private TimeSpan? _warmupInactivityTimeout = null;

    public TestConfigurationBuilder WithEventCount(int eventCount)
    {
        _eventCount = eventCount;
        return this;
    }

    public TestConfigurationBuilder WithApiDuration(TimeSpan apiDuration)
    {
        _apiDuration = apiDuration;
        return this;
    }

    public TestConfigurationBuilder WithApiWorkers(int apiWorkers)
    {
        _apiWorkers = apiWorkers;
        return this;
    }

    public TestConfigurationBuilder WithServicePort(int servicePort)
    {
        _servicePort = servicePort;
        return this;
    }

    public TestConfigurationBuilder WithDatabaseName(string databaseName)
    {
        _databaseName = databaseName;
        return this;
    }

    public TestConfigurationBuilder WithResultsFolder(string resultsFolder)
    {
        _resultsFolder = resultsFolder;
        return this;
    }

    public TestConfigurationBuilder WithRabbitMqContainerName(string rabbitMqContainerName)
    {
        _rabbitMqContainerName = rabbitMqContainerName;
        return this;
    }

    public TestConfigurationBuilder WithPostgresContainerName(string postgresContainerName)
    {
        _postgresContainerName = postgresContainerName;
        return this;
    }

    public TestConfigurationBuilder WithInactivityTimeout(TimeSpan inactivityTimeout)
    {
        _inactivityTimeout = inactivityTimeout;
        return this;
    }

    public TestConfigurationBuilder WithWarmupEventCount(int warmupEventCount)
    {
        _warmupEventCount = warmupEventCount;
        return this;
    }

    public TestConfigurationBuilder WithWarmupApiDuration(TimeSpan warmupApiDuration)
    {
        _warmupApiDuration = warmupApiDuration;
        return this;
    }

    public TestConfigurationBuilder WithWarmupInactivityTimeout(TimeSpan warmupInactivityTimeout)
    {
        _warmupInactivityTimeout = warmupInactivityTimeout;
        return this;
    }

    public TestConfiguration Build()
    {
        return new TestConfiguration(
            EventCount: _eventCount,
            ApiDuration: _apiDuration,
            ApiWorkers: _apiWorkers,
            InactivityTimeout: _inactivityTimeout,
            WarmupEventCount: _warmupEventCount,
            WarmupApiDuration: _warmupApiDuration,
            WarmupInactivityTimeout: _warmupInactivityTimeout,
            ServicePort: _servicePort,
            DatabaseName: _databaseName,
            ResultsFolder: _resultsFolder,
            RabbitMqContainerName: _rabbitMqContainerName,
            PostgresContainerName: _postgresContainerName);
    }
}
