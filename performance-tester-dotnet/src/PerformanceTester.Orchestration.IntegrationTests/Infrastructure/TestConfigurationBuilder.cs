using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.Reporting.ValueObjects;

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
    private TimeSpan _inactivityTimeout = TimeSpan.FromSeconds(120);
    private int _warmupEventCount = 50;
    private uint _warmupApiCallCount = 5;
    private TimeSpan _warmupInactivityTimeout = TimeSpan.FromSeconds(30);
    private int _maxConsecutiveApiFailures = 3;

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

    public TestConfigurationBuilder WithWarmupApiCallCount(uint warmupApiCallCount)
    {
        _warmupApiCallCount = warmupApiCallCount;
        return this;
    }

    public TestConfigurationBuilder WithWarmupInactivityTimeout(TimeSpan warmupInactivityTimeout)
    {
        _warmupInactivityTimeout = warmupInactivityTimeout;
        return this;
    }

    public TestConfigurationBuilder WithMaxConsecutiveApiFailures(int maxConsecutiveApiFailures)
    {
        _maxConsecutiveApiFailures = maxConsecutiveApiFailures;
        return this;
    }

    public TestConfiguration Build()
    {
        return new TestConfiguration(
            EventCount: Unwrap(EventCount.Create(_eventCount)),
            ApiDuration: ApiDuration.FromTimeSpan(_apiDuration),
            ApiWorkers: Unwrap(WorkerCount.Create(_apiWorkers)),
            ServicePort: Port.FromInt(_servicePort),
            DatabaseName: DatabaseName.FromString(_databaseName),
            InactivityTimeout: InactivityTimeout.FromTimeSpan(_inactivityTimeout),
            WarmupEventCount: WarmupEventsCount.FromInt(_warmupEventCount),
            WarmupApiCallCount: WarmupApiCallsCount.FromUint(_warmupApiCallCount),
            WarmupInactivityTimeout: InactivityTimeout.FromTimeSpan(_warmupInactivityTimeout),
            ResultsFolder: ResultsOutputFolder.FromString(_resultsFolder),
            RabbitMqContainerName: RabbitMqContainerName.FromString(_rabbitMqContainerName),
            PostgresContainerName: PostgresContainerName.FromString(_postgresContainerName),
            MaxConsecutiveApiFailures: _maxConsecutiveApiFailures);
    }

    private static T Unwrap<T, TError>(Result<T, TError> result) => result.Match(
            success: s => s.Value,
            failure: f => throw new ArgumentException($"Invalid test value: {f.Error}"));
}
