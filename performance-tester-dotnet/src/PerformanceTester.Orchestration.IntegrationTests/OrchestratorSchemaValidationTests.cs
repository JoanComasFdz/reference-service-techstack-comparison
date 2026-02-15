using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests;

/// <summary>
/// Tests that validate the structure and schema of TestReport data.
/// These tests verify that reports have all required fields populated with valid values.
/// Port range: 9940-9949 (using ports 9940, 9941, 9942, 9943, 9944)
/// </summary>
public sealed class OrchestratorSchemaValidationTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    /// <summary>
    /// Test 5.2: Verifies that the main report has all required fields populated correctly.
    /// Validates TestDate, TotalRuntimeSeconds, System, MonitoredProcess, Configuration, and Results.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_MainReport_ShouldHaveRequiredFields()
    {
        // Arrange
        const int testPort = 9940;
        const int eventCount = 100;
        const int warmupEventCount = 20;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var resultsFolder = $"./test-results-schema-5.2-{Guid.NewGuid():N}";

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

            // Assert - TestDate
            Assert.True(report.TestDate > DateTime.MinValue,
                "TestDate should be a valid date greater than DateTime.MinValue");
            Output.WriteLine($"TestDate: {report.TestDate:O}");

            // Assert - TotalRuntimeSeconds
            Assert.True(report.TotalRuntimeSeconds > 0,
                "TotalRuntimeSeconds should be greater than 0");
            Output.WriteLine($"TotalRuntimeSeconds: {report.TotalRuntimeSeconds:F2}s");

            // Assert - System (not null with valid fields)
            Assert.NotNull(report.System);
            Assert.NotEmpty(report.System.Os);
            Assert.NotNull(report.System.Cpu);
            Assert.True(report.System.Cpu.LogicalProcessors > 0,
                "CPU cores (LogicalProcessors) should be greater than 0");
            Assert.NotEmpty(report.System.Cpu.Model);
            Assert.NotNull(report.System.Ram);
            Assert.True(report.System.Ram.TotalGb > 0,
                "RAM totalGb should be greater than 0");
            Output.WriteLine($"System: {report.System.Os}, {report.System.Cpu.Model}, {report.System.Ram.TotalGb:F2} GB RAM");

            // Assert - MonitoredProcess
            Assert.NotNull(report.MonitoredProcess);
            Assert.True(report.MonitoredProcess.Pid > 0,
                "MonitoredProcess Pid should be greater than 0");
            Assert.NotEmpty(report.MonitoredProcess.Name);
            Output.WriteLine($"MonitoredProcess: {report.MonitoredProcess.Name} (PID: {report.MonitoredProcess.Pid})");

            // Assert - Configuration
            Assert.NotNull(report.Configuration);
            Assert.True(report.Configuration.NumEvents > 0,
                "Configuration NumEvents should be greater than 0");
            Assert.Equal(eventCount, report.Configuration.NumEvents);
            Output.WriteLine($"Configuration: {report.Configuration.NumEvents} events");

            // Assert - Results (all three phases present)
            Assert.NotNull(report.Results);
            Assert.NotNull(report.Results.Phase1Publish);
            Assert.NotNull(report.Results.Phase2Consume);
            Assert.NotNull(report.Results.Phase3Api);
            Output.WriteLine("Results: All three phases present (Phase1Publish, Phase2Consume, Phase3Api)");

            Output.WriteLine("All required fields validated successfully");

            // Cleanup results folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
            }
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 5.3: Verifies that phase timestamps are in chronological order.
    /// Validates that Phase1Start &lt; Phase1End, Phase2Start &lt; Phase2End, Phase3Start &lt; Phase3End,
    /// and that phases 1 and 2 run concurrently while phase 3 starts after phase 2.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_PhaseTimestamps_ShouldBeChronological()
    {
        // Arrange
        const int testPort = 9941;
        const int eventCount = 100;
        const int warmupEventCount = 20;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var resultsFolder = $"./test-results-schema-5.3-{Guid.NewGuid():N}";

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

            // Assert - Phase 1 timestamps are in order
            Assert.True(report.PhaseTimestamps.Phase1Start < report.PhaseTimestamps.Phase1End,
                $"Phase1Start ({report.PhaseTimestamps.Phase1Start:F3}s) should be less than Phase1End ({report.PhaseTimestamps.Phase1End:F3}s)");
            Output.WriteLine($"Phase 1: {report.PhaseTimestamps.Phase1Start:F3}s - {report.PhaseTimestamps.Phase1End:F3}s");

            // Assert - Phase 2 timestamps are in order
            Assert.True(report.PhaseTimestamps.Phase2Start < report.PhaseTimestamps.Phase2End,
                $"Phase2Start ({report.PhaseTimestamps.Phase2Start:F3}s) should be less than Phase2End ({report.PhaseTimestamps.Phase2End:F3}s)");
            Output.WriteLine($"Phase 2: {report.PhaseTimestamps.Phase2Start:F3}s - {report.PhaseTimestamps.Phase2End:F3}s");

            // Assert - Phase 3 timestamps are in order
            Assert.True(report.PhaseTimestamps.Phase3Start < report.PhaseTimestamps.Phase3End,
                $"Phase3Start ({report.PhaseTimestamps.Phase3Start:F3}s) should be less than Phase3End ({report.PhaseTimestamps.Phase3End:F3}s)");
            Output.WriteLine($"Phase 3: {report.PhaseTimestamps.Phase3Start:F3}s - {report.PhaseTimestamps.Phase3End:F3}s");

            // Assert - Phases 1 and 2 run concurrently (Phase2Start <= Phase1End)
            // Note: They start at the same time, so Phase2Start should be <= Phase1End
            Assert.True(report.PhaseTimestamps.Phase2Start <= report.PhaseTimestamps.Phase1End,
                $"Phase2Start ({report.PhaseTimestamps.Phase2Start:F3}s) should be <= Phase1End ({report.PhaseTimestamps.Phase1End:F3}s) indicating concurrent execution");
            Output.WriteLine("Phases 1 and 2 executed concurrently (Phase2Start <= Phase1End)");

            // Assert - Phase 3 starts after Phase 2 ends
            Assert.True(report.PhaseTimestamps.Phase3Start >= report.PhaseTimestamps.Phase2End,
                $"Phase3Start ({report.PhaseTimestamps.Phase3Start:F3}s) should be >= Phase2End ({report.PhaseTimestamps.Phase2End:F3}s) indicating sequential execution");
            Output.WriteLine("Phase 3 started after Phase 2 completed (Phase3Start >= Phase2End)");

            Output.WriteLine("All phase timestamps validated successfully");

            // Cleanup results folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
            }
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 5.4: Verifies that throughput samples are chronologically ordered.
    /// Each sample's ElapsedSeconds should be >= the previous sample's ElapsedSeconds.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_ThroughputSamples_ShouldBeChronologicallyOrdered()
    {
        // Arrange
        const int testPort = 9942;
        const int eventCount = 100;
        const int warmupEventCount = 20;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var resultsFolder = $"./test-results-schema-5.4-{Guid.NewGuid():N}";

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

            // Assert - EventsThroughputSamples should not be empty
            Assert.NotEmpty(report.EventsThroughputSamples);
            Output.WriteLine($"EventsThroughputSamples count: {report.EventsThroughputSamples.Count}");

            // Assert - EventsThroughputSamples are chronologically ordered
            var eventsSamples = report.EventsThroughputSamples.ToList();
            for (int i = 1; i < eventsSamples.Count; i++)
            {
                Assert.True(eventsSamples[i].ElapsedSeconds >= eventsSamples[i - 1].ElapsedSeconds,
                    $"EventsThroughputSamples[{i}].ElapsedSeconds ({eventsSamples[i].ElapsedSeconds:F3}s) should be >= EventsThroughputSamples[{i - 1}].ElapsedSeconds ({eventsSamples[i - 1].ElapsedSeconds:F3}s)");
            }
            Output.WriteLine("EventsThroughputSamples are chronologically ordered");

            // Assert - ApiThroughputSamples (if present) are chronologically ordered
            if (report.ApiThroughputSamples.Count > 0)
            {
                var apiSamples = report.ApiThroughputSamples.ToList();
                Output.WriteLine($"ApiThroughputSamples count: {apiSamples.Count}");

                for (int i = 1; i < apiSamples.Count; i++)
                {
                    Assert.True(apiSamples[i].ElapsedSeconds >= apiSamples[i - 1].ElapsedSeconds,
                        $"ApiThroughputSamples[{i}].ElapsedSeconds ({apiSamples[i].ElapsedSeconds:F3}s) should be >= ApiThroughputSamples[{i - 1}].ElapsedSeconds ({apiSamples[i - 1].ElapsedSeconds:F3}s)");
                }
                Output.WriteLine("ApiThroughputSamples are chronologically ordered");
            }
            else
            {
                Output.WriteLine("ApiThroughputSamples is empty (no API samples to validate)");
            }

            Output.WriteLine("All throughput samples validated as chronologically ordered");

            // Cleanup results folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
            }
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 5.5: Verifies that throughput samples have valid, positive intervals.
    /// </summary>
    /// <remarks>
    /// This test validates the deterministic properties of throughput sampling:
    /// 1. Multiple samples are collected during a test run
    /// 2. All intervals between samples are positive (time moves forward)
    /// 3. Intervals are within a reasonable range (not stuck at 0, not absurdly large)
    ///
    /// Note: We intentionally do NOT test for exact 500ms intervals because:
    /// - The 500ms interval is an implementation detail, not a business requirement
    /// - Timing-based assertions are inherently non-deterministic due to system load,
    ///   container scheduling, k6 startup overhead, and GC pauses
    /// - Small sample counts make percentage thresholds statistically meaningless
    ///
    /// If precise interval timing needs testing, it should be done via unit tests
    /// with mocked timers, not integration tests.
    /// </remarks>
    [Fact]
    public async Task RunTestAsync_ThroughputSamples_ShouldHavePositiveIntervals()
    {
        // Arrange
        const int testPort = 9943;
        const int eventCount = 100;
        const int warmupEventCount = 20;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var resultsFolder = $"./test-results-schema-5.5-{Guid.NewGuid():N}";

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

            // Assert 1: API throughput samples were collected
            var apiSamples = report.ApiThroughputSamples.ToList();
            Output.WriteLine($"ApiThroughputSamples count: {apiSamples.Count}");
            Assert.True(
                apiSamples.Count >= 2,
                $"Expected at least 2 API throughput samples, but got {apiSamples.Count}");

            // Assert 2: All intervals between samples are positive (time moves forward)
            var intervals = new List<double>();
            for (int i = 1; i < apiSamples.Count; i++)
            {
                var interval = apiSamples[i].ElapsedSeconds - apiSamples[i - 1].ElapsedSeconds;
                intervals.Add(interval);

                Assert.True(interval > 0,
                    $"Interval {i} should be positive but was {interval:F3}s " +
                    $"(sample[{i - 1}]={apiSamples[i - 1].ElapsedSeconds:F3}s, sample[{i}]={apiSamples[i].ElapsedSeconds:F3}s)");
            }

            Output.WriteLine($"Validated {intervals.Count} intervals - all positive");

            // Assert 3: Intervals are within a reasonable range (sanity check)
            // Not testing exact 500ms - just ensuring samples aren't stuck or absurdly spaced
            const double maxReasonableInterval = 10.0; // No interval should be > 10 seconds
            foreach (var (interval, index) in intervals.Select((v, i) => (v, i)))
            {
                Assert.True(interval <= maxReasonableInterval,
                    $"Interval {index + 1} ({interval:F3}s) exceeds maximum reasonable interval of {maxReasonableInterval}s");
            }

            // Log intervals for debugging (informational, not asserted)
            Output.WriteLine("Interval details:");
            for (int i = 0; i < intervals.Count; i++)
            {
                Output.WriteLine($"  Interval {i + 1}: {intervals[i]:F3}s");
            }

            var avgInterval = intervals.Average();
            Output.WriteLine($"Average interval: {avgInterval:F3}s");
            Output.WriteLine("All throughput sample intervals validated successfully");

            // Cleanup results folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
            }
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    /// <summary>
    /// Test 5.6: Verifies that resource metrics have positive/non-negative values.
    /// Validates ProcessResourceSamples, RabbitMqResourceSamples, and PostgresResourceSamples.
    /// </summary>
    [Fact]
    public async Task RunTestAsync_ResourceMetrics_ShouldHavePositiveValues()
    {
        // Arrange
        const int testPort = 9944;
        const int eventCount = 100;
        const int warmupEventCount = 20;

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: eventCount,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var resultsFolder = $"./test-results-schema-5.6-{Guid.NewGuid():N}";

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

            // Assert - ProcessResourceSamples should not be empty
            Assert.NotEmpty(report.ProcessResourceSamples);
            Output.WriteLine($"ProcessResourceSamples count: {report.ProcessResourceSamples.Count}");

            // Assert - All ProcessResourceSamples have non-negative values
            foreach (var sample in report.ProcessResourceSamples)
            {
                Assert.True(sample.MemoryRssMb >= 0,
                    $"ProcessResourceSample.MemoryRssMb ({sample.MemoryRssMb}) should be >= 0");
                Assert.True(sample.CpuPercent >= 0,
                    $"ProcessResourceSample.CpuPercent ({sample.CpuPercent}) should be >= 0");
                Assert.True(sample.Threads >= 0,
                    $"ProcessResourceSample.Threads ({sample.Threads}) should be >= 0");
                Assert.True(sample.ElapsedSeconds >= 0,
                    $"ProcessResourceSample.ElapsedSeconds ({sample.ElapsedSeconds}) should be >= 0");
            }
            Output.WriteLine("All ProcessResourceSamples have non-negative values");

            // Assert - RabbitMqResourceSamples should not be empty
            Assert.NotEmpty(report.RabbitMqResourceSamples);
            Output.WriteLine($"RabbitMqResourceSamples count: {report.RabbitMqResourceSamples.Count}");

            // Assert - All RabbitMqResourceSamples have non-negative values
            foreach (var sample in report.RabbitMqResourceSamples)
            {
                Assert.True(sample.MemoryMb >= 0,
                    $"RabbitMqResourceSample.MemoryMb ({sample.MemoryMb}) should be >= 0");
                Assert.True(sample.CpuPercent >= 0,
                    $"RabbitMqResourceSample.CpuPercent ({sample.CpuPercent}) should be >= 0");
                Assert.True(sample.ElapsedSeconds >= 0,
                    $"RabbitMqResourceSample.ElapsedSeconds ({sample.ElapsedSeconds}) should be >= 0");
            }
            Output.WriteLine("All RabbitMqResourceSamples have non-negative values");

            // Assert - PostgresResourceSamples should not be empty
            Assert.NotEmpty(report.PostgresResourceSamples);
            Output.WriteLine($"PostgresResourceSamples count: {report.PostgresResourceSamples.Count}");

            // Assert - All PostgresResourceSamples have non-negative values
            foreach (var sample in report.PostgresResourceSamples)
            {
                Assert.True(sample.MemoryMb >= 0,
                    $"PostgresResourceSample.MemoryMb ({sample.MemoryMb}) should be >= 0");
                Assert.True(sample.CpuPercent >= 0,
                    $"PostgresResourceSample.CpuPercent ({sample.CpuPercent}) should be >= 0");
                Assert.True(sample.ElapsedSeconds >= 0,
                    $"PostgresResourceSample.ElapsedSeconds ({sample.ElapsedSeconds}) should be >= 0");
            }
            Output.WriteLine("All PostgresResourceSamples have non-negative values");

            Output.WriteLine("All resource metrics validated with positive/non-negative values");

            // Cleanup results folder
            if (Directory.Exists(resultsFolder))
            {
                Directory.Delete(resultsFolder, recursive: true);
            }
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
