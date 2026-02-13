using System.Text.Json;
using PerformanceTester.Reporting.IntegrationTests.Builders;
using PerformanceTester.Reporting.IntegrationTests.Infrastructure;
using PerformanceTester.Reporting.ValueObjects;
using Xunit.Abstractions;
using static PerformanceTester.Reporting.IntegrationTests.ContractTests.ContractAssertions;

namespace PerformanceTester.Reporting.IntegrationTests.ContractTests;

/// <summary>
/// Contract tests for main report JSON (test-report-*.json).
/// Verifies 100% compatibility with Python performance-tester output.
///
/// Reference: Python generates this structure in report_generator.py
/// </summary>
public sealed class MainReportContractTests : IntegrationTest
{
    public MainReportContractTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Comprehensive contract test verifying all aspects of the main report JSON.
    /// </summary>
    [Fact]
    public async Task MainReport_ShouldConformToContract()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Create test data with KNOWN, PREDICTABLE values
        // ═══════════════════════════════════════════════════════════════
        var testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);

        var phaseTimestamps = new PhaseTimestamps
        {
            Phase1Start = 9.445,
            Phase1End = 9.533,
            Phase2Start = 9.536,
            Phase2End = 13.342,
            Phase3Start = 13.342,
            Phase3End = 18.843
        };

        var configuration = new TestConfiguration
        {
            NumEvents = 1000,
            ApiDuration = "5s",
            ApiConcurrentWorkers = 1,
            RabbitmqExchange = "referenceservice.comparison",
            ConsumerQueue = "service-tester",
            ApiEndpoint = "http://localhost:8092/kpi",
            PublishEventType = "instrument.status.changed",
            ConsumeEventType = "instrumentstatus.kpi.updated"
        };

        var results = new TestResults
        {
            Phase1Publish = new PublishResults
            {
                DurationSeconds = 0.088,
                ThroughputEventsPerSec = 11377.5
            },
            Phase2Consume = new ConsumeResults
            {
                DurationSeconds = 3.806,
                ThroughputEventsPerSec = 262.71
            },
            Phase3Api = new ApiResults
            {
                DurationSeconds = 5.486,
                TotalRequests = 4139,
                ThroughputCallsPerSec = 754.51,
                SuccessCount = 4139,
                SuccessPercentage = 100.0,
                ErrorCount = 0,
                ErrorPercentage = 0.0
            }
        };

        var systemInfo = new SystemInfo
        {
            Os = "Linux",
            OsRelease = "6.6.87.2-microsoft-standard-WSL2",
            OsVersion = "#1 SMP PREEMPT_DYNAMIC Thu Jun  5 18:30:46 UTC 2025",
            WslVersion = "WSL2",
            Cpu = new CpuInfo
            {
                Model = "Intel(R) Core(TM) Ultra 7 255H",
                LogicalProcessors = 16,
                PhysicalProcessors = 1,
                SpeedMhz = 3686.4
            },
            Ram = new RamInfo
            {
                TotalGb = 15.31
            },
            Disks = new List<DiskInfo>
            {
                new DiskInfo
                {
                    Name = "sdd",
                    Size = "1.0T",
                    Type = "SSD",
                    Model = "Virtual Disk    "
                }
            }
        };

        var testReport = new TestReportBuilder()
            .WithTestDate(testDate)
            .WithProcessName("dotnet9ReferenceService")
            .WithRuntime(18.843)
            .WithPhaseTimestamps(phaseTimestamps)
            .WithConfiguration(configuration)
            .WithResults(results)
            .WithSystemInfo(systemInfo)
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            // ═══════════════════════════════════════════════════════════
            // ACT: Generate the report
            // ═══════════════════════════════════════════════════════════
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var files = Directory.GetFiles(outputDirectory, "*.json")
                .Where(f => !f.Contains("throughput") && !f.Contains("metrics"))
                .ToList();
            Assert.Single(files);

            var filePath = files.First();
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ═══════════════════════════════════════════════════════════
            // ASSERT: Verify EVERY aspect of the contract
            // ═══════════════════════════════════════════════════════════

            // --- ROOT LEVEL FIELDS ---
            AssertFieldExists(root, "test_date");
            AssertTimestampFormat(root, "test_date", TimestampFormat.TestDate);
            AssertStringValue(root, "test_date", "2025-11-13 14:25:30");

            AssertFieldExists(root, "total_runtime_seconds");
            AssertNumericValue(root, "total_runtime_seconds", 18.843, 3);

            // --- ROOT FIELD ORDER (as per Python) ---
            AssertFieldOrder(rawJson, "test_date", "total_runtime_seconds", "system", "phase_timestamps", "monitored_process", "configuration", "results");

            // --- SYSTEM OBJECT ---
            var system = AssertObjectExists(root, "system");
            AssertStringValue(system, "os", "Linux");
            AssertStringValue(system, "os_release", "6.6.87.2-microsoft-standard-WSL2");
            AssertFieldExists(system, "os_version");
            AssertFieldExists(system, "wsl_version");

            // System.cpu
            var cpu = AssertObjectExists(system, "cpu");
            AssertStringValue(cpu, "model", "Intel(R) Core(TM) Ultra 7 255H");
            AssertIntegerValue(cpu, "logical_processors", 16);
            AssertIntegerValue(cpu, "physical_processors", 1);
            AssertFieldExists(cpu, "speed_mhz");

            // System.ram
            var ram = AssertObjectExists(system, "ram");
            AssertFieldExists(ram, "total_gb");

            // System.disks (array)
            AssertArrayMinCount(system, "disks", 1);

            // --- PHASE_TIMESTAMPS OBJECT ---
            var phases = AssertObjectExists(root, "phase_timestamps");
            AssertFieldExists(phases, "phase1_start");
            AssertFieldExists(phases, "phase1_end");
            AssertFieldExists(phases, "phase2_start");
            AssertFieldExists(phases, "phase2_end");
            AssertFieldExists(phases, "phase3_start");
            AssertFieldExists(phases, "phase3_end");
            AssertNumericValue(phases, "phase1_start", 9.445, 3);
            AssertNumericValue(phases, "phase1_end", 9.533, 3);

            // --- MONITORED_PROCESS OBJECT ---
            var monitoredProcess = AssertObjectExists(root, "monitored_process");

            // CONTRACT: Python produces { "name": ..., "pid": ..., "port": ... }
            // The order should be: name, pid, port (per reference file: pid, name, port)
            AssertFieldExists(monitoredProcess, "pid");
            AssertFieldExists(monitoredProcess, "name");
            AssertStringValue(monitoredProcess, "name", "dotnet9ReferenceService");

            // --- CONFIGURATION OBJECT ---
            var config = AssertObjectExists(root, "configuration");
            AssertIntegerValue(config, "num_events", 1000);
            AssertStringValue(config, "api_duration", "5s");
            AssertIntegerValue(config, "api_concurrent_workers", 1);
            AssertStringValue(config, "rabbitmq_exchange", "referenceservice.comparison");
            AssertStringValue(config, "consumer_queue", "service-tester");
            AssertStringValue(config, "api_endpoint", "http://localhost:8092/kpi");
            AssertStringValue(config, "publish_event_type", "instrument.status.changed");
            AssertStringValue(config, "consume_event_type", "instrumentstatus.kpi.updated");

            // --- RESULTS OBJECT ---
            var resultsObj = AssertObjectExists(root, "results");

            // results.phase1_publish
            var phase1 = AssertObjectExists(resultsObj, "phase1_publish");
            AssertNumericValue(phase1, "duration_seconds", 0.088, 3);
            AssertNumericValue(phase1, "throughput_events_per_sec", 11377.5, 2);

            // results.phase2_consume
            var phase2 = AssertObjectExists(resultsObj, "phase2_consume");
            AssertNumericValue(phase2, "duration_seconds", 3.806, 3);
            AssertNumericValue(phase2, "throughput_events_per_sec", 262.71, 2);

            // results.phase3_api
            var phase3 = AssertObjectExists(resultsObj, "phase3_api");
            AssertNumericValue(phase3, "duration_seconds", 5.486, 3);
            AssertIntegerValue(phase3, "total_requests", 4139);
            AssertNumericValue(phase3, "throughput_calls_per_sec", 754.51, 2);
            AssertIntegerValue(phase3, "success_count", 4139);
            AssertNumericValue(phase3, "success_percentage", 100.0, 1);
            AssertIntegerValue(phase3, "error_count", 0);
            AssertNumericValue(phase3, "error_percentage", 0.0, 1);

            // --- VERIFY SNAKE_CASE (NOT camelCase) ---
            AssertFieldNotExists(root, "testDate");
            AssertFieldNotExists(root, "totalRuntimeSeconds");
            AssertFieldNotExists(root, "monitoredProcess");
            AssertFieldNotExists(root, "phaseTimestamps");

            Output.WriteLine("✓ Main report contract verification passed");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: monitored_process must include port field (per Python reference).
    /// </summary>
    [Fact]
    public async Task MainReport_MonitoredProcess_ShouldIncludePort()
    {
        var testReport = new TestReportBuilder()
            .WithProcessName("testService")
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.json")
                .First(f => !f.Contains("throughput") && !f.Contains("metrics"));
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            var monitoredProcess = doc.RootElement.GetProperty("monitored_process");

            // CONTRACT: Python reference shows monitored_process has: pid, name, port
            AssertFieldExists(monitoredProcess, "port", "monitored_process");

            Output.WriteLine("✓ monitored_process.port field verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: monitored_process field order should match Python.
    /// Python reference shows: { "name": ..., "pid": ..., "port": ... }
    /// </summary>
    [Fact]
    public async Task MainReport_MonitoredProcess_ShouldHaveCorrectFieldOrder()
    {
        var testReport = new TestReportBuilder()
            .WithProcessName("testService")
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.json")
                .First(f => !f.Contains("throughput") && !f.Contains("metrics"));
            var rawJson = await File.ReadAllTextAsync(filePath);

            // Extract just the monitored_process section
            var mpStart = rawJson.IndexOf("\"monitored_process\"");
            var mpEnd = rawJson.IndexOf("}", mpStart) + 1;
            var mpSection = rawJson.Substring(mpStart, mpEnd - mpStart);

            // CONTRACT: Python reference shows order: name, pid, port
            AssertFieldOrder(mpSection, "name", "pid", "port");

            Output.WriteLine("✓ monitored_process field order verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }

    /// <summary>
    /// Contract test: Decimal precision must match Python exactly.
    /// </summary>
    [Fact]
    public async Task MainReport_DecimalPrecision_ShouldMatchPythonContract()
    {
        var testReport = new TestReportBuilder()
            .WithRuntime(18.843333)  // Should round to 18.843 (3 decimals)
            .WithResults(new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 0.0879999,  // Should round to 0.088 (3 decimals)
                    ThroughputEventsPerSec = 11377.516  // Should round to 11377.52 (2 decimals)
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 3.806,
                    ThroughputEventsPerSec = 262.71
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = 5.486,
                    TotalRequests = 4139,
                    ThroughputCallsPerSec = 754.515,  // Should round to 754.52 (2 decimals)
                    SuccessCount = 4139,
                    SuccessPercentage = 99.97,  // Should round to 100.0 (1 decimal)
                    ErrorCount = 0,
                    ErrorPercentage = 0.03
                }
            })
            .WithSystemInfo(SystemInfoBuilder.CreateDefault())
            .Build();

        var outputDirectory = System.FileSystem.CreateTempDirectory("contract-test");
        try
        {
            await System.Reporting.ReportGenerator.GenerateReportAsync(ResultsOutputFolder.FromString(outputDirectory), testReport);

            var filePath = Directory.GetFiles(outputDirectory, "*.json")
                .First(f => !f.Contains("throughput") && !f.Contains("metrics"));
            var rawJson = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // Time in seconds: 3 decimals
            AssertDecimalPrecision(root, "total_runtime_seconds", 3, rawJson);

            var phase1 = root.GetProperty("results").GetProperty("phase1_publish");
            AssertDecimalPrecision(phase1, "duration_seconds", 3, rawJson);

            // Throughput (per sec): 2 decimals
            AssertDecimalPrecision(phase1, "throughput_events_per_sec", 2, rawJson);

            Output.WriteLine("✓ Decimal precision contract verified");
        }
        finally
        {
            System.FileSystem.CleanupTempDirectory(outputDirectory);
        }
    }
}
