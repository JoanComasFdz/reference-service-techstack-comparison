using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.SystemMonitoring;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Transforms raw TestResult and collected metrics into the final TestReport.
/// </summary>
internal static class TestReportBuilder
{
    public static TestReport Build(
        TestResult testResult,
        TestConfiguration config,
        IReadOnlyCollection<EventThroughputSample> throughputSamples,
        IReadOnlyCollection<ProcessMetrics> processMetrics,
        IReadOnlyCollection<SystemMetrics> systemMetrics,
        IReadOnlyCollection<DockerMetrics> rabbitMqMetrics,
        IReadOnlyCollection<DockerMetrics> postgresMetrics,
        SystemInfo? systemInfo)
    {
        // Calculate phase durations
        var warmupDuration = testResult.WarmupEndTime - testResult.WarmupStartTime;
        var eventTestDuration = testResult.EventTestEndTime - testResult.EventTestStartTime;
        var apiTestDuration = testResult.ApiTestEndTime - testResult.ApiTestStartTime;
        var totalDuration = testResult.TestEndTime - testResult.TestStartTime;

        // Get process name from first sample
        var serviceName = processMetrics.FirstOrDefault()?.ProcessName ?? "unknown";

        // Calculate elapsed times from test start
        var testStartTime = testResult.TestStartTime;
        var eventTestElapsedStart = (testResult.EventTestStartTime - testStartTime).TotalSeconds;
        var phase1Start = eventTestElapsedStart;  // Publisher starts when event test starts
        var phase1End = eventTestElapsedStart + testResult.PublishMetrics.Duration.TotalSeconds;  // Publisher completes after its duration
        var phase2Start = eventTestElapsedStart;  // Consumer starts concurrently with publisher
        var phase2End = (testResult.EventTestEndTime - testStartTime).TotalSeconds;
        var phase3Start = (testResult.ApiTestStartTime - testStartTime).TotalSeconds;
        var phase3End = (testResult.ApiTestEndTime - testStartTime).TotalSeconds;

        // Convert metrics to report sample formats
        var processResourceSamples = processMetrics.Select(m => new ProcessResourceSample
        {
            Timestamp = m.Timestamp.UtcDateTime,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryRssMb = m.MemoryMB,
            Threads = m.ThreadCount
        }).ToList();

        var rabbitMqResourceSamples = rabbitMqMetrics.Select(m => new ContainerResourceSample
        {
            Timestamp = m.Timestamp,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryMb = m.MemoryMB
        }).ToList();

        var postgresResourceSamples = postgresMetrics.Select(m => new ContainerResourceSample
        {
            Timestamp = m.Timestamp,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryMb = m.MemoryMB
        }).ToList();

        // Convert system metrics to SystemResourceSample
        var systemResourceSamples = systemMetrics.Select(m => new SystemResourceSample
        {
            Timestamp = m.Timestamp.UtcDateTime,
            ElapsedSeconds = (m.Timestamp - testStartTime).TotalSeconds,
            CpuPercent = m.CpuPercent,
            MemoryUsedMb = m.MemoryUsedMb,
            MemoryTotalMb = m.MemoryTotalMb,
            MemoryPercent = m.MemoryPercent
        }).ToList();

        // Convert event throughput samples
        var eventThroughputSamples = throughputSamples.Select(s => new Reporting.ThroughputMetricSample
        {
            Timestamp = s.Timestamp.UtcDateTime,
            ElapsedSeconds = (s.Timestamp - testResult.TestStartTime).TotalSeconds,
            Rate = s.ThroughputEventsPerSecond,
            CumulativeCount = s.CumulativeEventCount
        }).ToList();

        // Convert API throughput samples
        var apiThroughputSamples = testResult.ApiLoadTestResult.ThroughputSamples.Select(s => new Reporting.ThroughputMetricSample
        {
            Timestamp = s.Timestamp.UtcDateTime,
            ElapsedSeconds = (s.Timestamp - testResult.TestStartTime).TotalSeconds,
            Rate = s.RequestsPerSecond,
            CumulativeCount = s.CumulativeRequestCount
        }).ToList();

        return new TestReport
        {
            TestDate = testResult.TestStartTime,
            TotalRuntimeSeconds = totalDuration.TotalSeconds,
            PhaseTimestamps = new PhaseTimestamps
            {
                Phase1Start = phase1Start,
                Phase1End = phase1End,
                Phase2Start = phase2Start,
                Phase2End = phase2End,
                Phase3Start = phase3Start,
                Phase3End = phase3End
            },
            MonitoredProcess = new MonitoredProcess
            {
                Name = serviceName,
                Pid = testResult.ServiceProcessId,
                Port = config.ServicePort.Value
            },
            System = systemInfo ?? throw new InvalidOperationException("System info is required"),
            Configuration = new Reporting.TestConfiguration
            {
                NumEvents = config.EventCount.Value,
                ApiDuration = config.ApiDuration.Value.TotalHours >= 1
                    ? $"{(int)config.ApiDuration.Value.TotalHours}h"
                    : config.ApiDuration.Value.TotalMinutes >= 1
                        ? $"{(int)config.ApiDuration.Value.TotalMinutes}m"
                        : $"{(int)config.ApiDuration.Value.TotalSeconds}s",
                ApiConcurrentWorkers = config.ApiWorkers.Value,
                RabbitmqExchange = "referenceservice.comparison",
                ConsumerQueue = "instrument-status-changed",
                ApiEndpoint = config.ApiUrl,
                PublishEventType = "com.referenceservice.instrument.status.changed",
                ConsumeEventType = "com.referenceservice.instrumentstatus.kpi.updated"
            },
            Results = new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = testResult.PublishMetrics.Duration.TotalSeconds,
                    ThroughputEventsPerSec = testResult.PublishMetrics.EventsPerSecond
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = eventTestDuration.TotalSeconds,
                    ThroughputEventsPerSec = config.EventCount.Value / eventTestDuration.TotalSeconds
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = testResult.ApiLoadTestResult.TotalDuration.TotalSeconds,
                    TotalRequests = testResult.ApiLoadTestResult.TotalRequests,
                    ThroughputCallsPerSec = testResult.ApiLoadTestResult.RequestsPerSecond,
                    SuccessCount = testResult.ApiLoadTestResult.TotalRequests - testResult.ApiLoadTestResult.FailedRequests,
                    SuccessPercentage = ((testResult.ApiLoadTestResult.TotalRequests - testResult.ApiLoadTestResult.FailedRequests) / (double)testResult.ApiLoadTestResult.TotalRequests) * 100,
                    ErrorCount = testResult.ApiLoadTestResult.FailedRequests,
                    ErrorPercentage = (testResult.ApiLoadTestResult.FailedRequests / (double)testResult.ApiLoadTestResult.TotalRequests) * 100,
                    WasAborted = testResult.ApiLoadTestResult.WasAborted,
                    AbortReason = testResult.ApiLoadTestResult.AbortReason
                }
            },
            EventsThroughputSamples = eventThroughputSamples,
            ApiThroughputSamples = apiThroughputSamples,
            ProcessResourceSamples = processResourceSamples,
            SystemResourceSamples = systemResourceSamples,
            RabbitMqResourceSamples = rabbitMqResourceSamples,
            PostgresResourceSamples = postgresResourceSamples
        };
    }
}
