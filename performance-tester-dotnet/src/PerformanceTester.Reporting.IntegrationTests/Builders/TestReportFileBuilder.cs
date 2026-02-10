using System.Text.Json;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Fluent builder for creating test report data files matching Python output format.
/// Serializes TestReport components to JSON files with snake_case naming convention.
/// </summary>
public class TestReportFileBuilder
{
    private readonly string _testDir;
    private readonly string _processName;
    private readonly DateTime _baseTime;

    // All reports are nullable - null means excluded, non-null means included
    private ThroughputReport? _eventsReport;
    private ThroughputReport? _apiReport;
    private ProcessResourceMetricsReport? _serviceReport;
    private ResourceMetricsReport? _rabbitmqReport;
    private ResourceMetricsReport? _postgresReport;
    private ResourceMetricsReport? _systemReport;

    /// <summary>
    /// Creates a new test report file builder.
    /// By default, no files are included - use WithXxx() methods to explicitly add data files.
    /// </summary>
    /// <param name="testDir">Directory to write files to</param>
    /// <param name="testReport">Test report containing metadata and configuration</param>
    public TestReportFileBuilder(string testDir, TestReport testReport)
    {
        _testDir = testDir;
        _processName = testReport.MonitoredProcess.Name;
        _baseTime = testReport.TestDate;
    }

    /// <summary>
    /// Creates a new test report file builder with explicit process name.
    /// By default, no files are included - use WithXxx() methods to explicitly add data files.
    /// </summary>
    /// <param name="testDir">Directory to write files to</param>
    /// <param name="processName">Process name for base path</param>
    /// <param name="baseTime">Base timestamp for data generation</param>
    public TestReportFileBuilder(string testDir, string processName, DateTime baseTime)
    {
        _testDir = testDir;
        _processName = processName;
        _baseTime = baseTime;
    }

    /// <summary>
    /// Include events throughput file with provided data.
    /// </summary>
    public TestReportFileBuilder WithEvents(ThroughputReport eventsReport)
    {
        _eventsReport = eventsReport;
        return this;
    }

    /// <summary>
    /// Include API throughput file with provided data.
    /// </summary>
    public TestReportFileBuilder WithApi(ThroughputReport apiReport)
    {
        _apiReport = apiReport;
        return this;
    }

    /// <summary>
    /// Include service resource metrics file with provided data.
    /// </summary>
    public TestReportFileBuilder WithService(ProcessResourceMetricsReport serviceReport)
    {
        _serviceReport = serviceReport;
        return this;
    }

    /// <summary>
    /// Include RabbitMQ resource metrics file with provided data.
    /// </summary>
    public TestReportFileBuilder WithRabbitMq(ResourceMetricsReport rabbitmqReport)
    {
        _rabbitmqReport = rabbitmqReport;
        return this;
    }

    /// <summary>
    /// Include PostgreSQL resource metrics file with provided data.
    /// </summary>
    public TestReportFileBuilder WithPostgres(ResourceMetricsReport postgresReport)
    {
        _postgresReport = postgresReport;
        return this;
    }

    /// <summary>
    /// Include system resource metrics file with provided data.
    /// </summary>
    public TestReportFileBuilder WithSystem(ResourceMetricsReport systemReport)
    {
        _systemReport = systemReport;
        return this;
    }

    /// <summary>
    /// Builds and writes all configured data files to disk.
    /// Generates 0-6 JSON files based on configuration.
    /// Uses synchronous I/O as this is a test utility writing small files.
    /// </summary>
    /// <returns>Path to the expected chart PNG file</returns>
    public string Build()
    {
        // Generate base path matching Python format: test-report-{timestamp}-{processname}
        var basePath = Path.Combine(_testDir,
            $"test-report-20250113_142530-{_processName.ToLowerInvariant()}");

        // Configure JSON options for snake_case output (matching Python)
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            Converters = { new Iso8601DateTimeConverter() }
        };

        // Write throughput reports
        if (_eventsReport is not null)
        {
            var eventsJson = JsonSerializer.Serialize(_eventsReport, jsonOptions);
            File.WriteAllText($"{basePath}.events-throughput.json", eventsJson);
        }

        if (_apiReport is not null)
        {
            var apiJson = JsonSerializer.Serialize(_apiReport, jsonOptions);
            File.WriteAllText($"{basePath}.api-throughput.json", apiJson);
        }

        // Write resource metrics reports
        if (_serviceReport is not null)
        {
            var serviceJson = JsonSerializer.Serialize(_serviceReport, jsonOptions);
            File.WriteAllText($"{basePath}.resource-metrics.json", serviceJson);
        }

        if (_rabbitmqReport is not null)
        {
            var rabbitmqJson = JsonSerializer.Serialize(_rabbitmqReport, jsonOptions);
            File.WriteAllText($"{basePath}.rabbitmq-metrics.json", rabbitmqJson);
        }

        if (_postgresReport is not null)
        {
            var postgresJson = JsonSerializer.Serialize(_postgresReport, jsonOptions);
            File.WriteAllText($"{basePath}.postgres-metrics.json", postgresJson);
        }

        if (_systemReport is not null)
        {
            var systemJson = JsonSerializer.Serialize(_systemReport, jsonOptions);
            File.WriteAllText($"{basePath}.system-metrics.json", systemJson);
        }

        // Return expected chart PNG path
        return $"{basePath}.chart.png";
    }
}
