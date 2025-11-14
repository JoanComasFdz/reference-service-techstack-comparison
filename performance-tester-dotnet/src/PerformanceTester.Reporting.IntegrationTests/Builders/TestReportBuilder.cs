namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Fluent builder for creating TestReport instances in integration tests.
/// Provides sensible defaults while allowing targeted customization.
/// </summary>
public class TestReportBuilder
{
    private string _processName = "testService";
    private double _runtime = 100.0;
    private double _avgCpu = 25.0;
    private double _avgMemory = 150.0;
    private double _eventsThroughput = 1000.0;
    private double _eventsCv = 5.0;
    private double _apiThroughput = 100.0;
    private double _apiCv = 3.0;
    private DateTime _testDate = new DateTime(2025, 11, 13, 14, 25, 30, DateTimeKind.Utc);
    
    // Optional overrides for advanced customization
    private PhaseTimestamps? _phaseTimestamps;
    private TestResults? _results;
    private TestConfiguration? _configuration;
    private SystemInfo? _systemInfo;
    private IReadOnlyList<ThroughputSample>? _eventsThroughputSamples;
    private IReadOnlyList<ThroughputSample>? _apiThroughputSamples;
    private IReadOnlyList<ProcessResourceSample>? _processResourceSamples;
    private IReadOnlyList<ContainerResourceSample>? _systemResourceSamples;
    private IReadOnlyList<ContainerResourceSample>? _rabbitmqResourceSamples;
    private IReadOnlyList<ContainerResourceSample>? _postgresResourceSamples;

    public TestReportBuilder WithProcessName(string name)
    {
        _processName = name;
        return this;
    }

    public TestReportBuilder WithRuntime(double seconds)
    {
        _runtime = seconds;
        return this;
    }

    public TestReportBuilder WithCpu(double avgPercent)
    {
        _avgCpu = avgPercent;
        return this;
    }

    public TestReportBuilder WithMemory(double avgMb)
    {
        _avgMemory = avgMb;
        return this;
    }

    public TestReportBuilder WithEventsThroughput(double rate, double cv = 5.0)
    {
        _eventsThroughput = rate;
        _eventsCv = cv;
        return this;
    }

    public TestReportBuilder WithApiThroughput(double rate, double cv = 3.0)
    {
        _apiThroughput = rate;
        _apiCv = cv;
        return this;
    }

    public TestReportBuilder WithTestDate(DateTime date)
    {
        _testDate = date;
        return this;
    }

    public TestReportBuilder WithPhaseTimestamps(PhaseTimestamps timestamps)
    {
        _phaseTimestamps = timestamps;
        return this;
    }

    public TestReportBuilder WithResults(TestResults results)
    {
        _results = results;
        return this;
    }

    public TestReportBuilder WithConfiguration(TestConfiguration configuration)
    {
        _configuration = configuration;
        return this;
    }

    public TestReportBuilder WithSystemInfo(SystemInfo systemInfo)
    {
        _systemInfo = systemInfo;
        return this;
    }

    public TestReportBuilder WithEventsThroughputSamples(IReadOnlyList<ThroughputSample> samples)
    {
        _eventsThroughputSamples = samples;
        return this;
    }

    public TestReportBuilder WithApiThroughputSamples(IReadOnlyList<ThroughputSample> samples)
    {
        _apiThroughputSamples = samples;
        return this;
    }

    public TestReportBuilder WithProcessResourceSamples(IReadOnlyList<ProcessResourceSample> samples)
    {
        _processResourceSamples = samples;
        return this;
    }

    public TestReportBuilder WithSystemResourceSamples(IReadOnlyList<ContainerResourceSample> samples)
    {
        _systemResourceSamples = samples;
        return this;
    }

    public TestReportBuilder WithRabbitmqResourceSamples(IReadOnlyList<ContainerResourceSample> samples)
    {
        _rabbitmqResourceSamples = samples;
        return this;
    }

    public TestReportBuilder WithPostgresResourceSamples(IReadOnlyList<ContainerResourceSample> samples)
    {
        _postgresResourceSamples = samples;
        return this;
    }

    public TestReportBuilder WithAllContainerSamples(IReadOnlyList<ContainerResourceSample> samples)
    {
        _systemResourceSamples = samples;
        _rabbitmqResourceSamples = samples;
        _postgresResourceSamples = samples;
        return this;
    }

    public TestReport Build()
    {
        // Use custom samples if provided, otherwise generate defaults
        var eventsSamples = _eventsThroughputSamples ?? ThroughputSampleBuilder.Create(_testDate, _eventsThroughput, _eventsCv);
        var apiSamples = _apiThroughputSamples ?? ThroughputSampleBuilder.Create(_testDate.AddSeconds(30), _apiThroughput, _apiCv);
        var processSamples = _processResourceSamples ?? ResourceSampleBuilder.CreateProcess(_testDate, _avgCpu, _avgMemory);
        var systemSamples = _systemResourceSamples ?? ResourceSampleBuilder.CreateContainer(_testDate, count: 5);
        var rabbitmqSamples = _rabbitmqResourceSamples ?? systemSamples;
        var postgresSamples = _postgresResourceSamples ?? systemSamples;

        return new TestReport
        {
            TestDate = _testDate,
            TotalRuntimeSeconds = _runtime,

            PhaseTimestamps = _phaseTimestamps ?? new PhaseTimestamps
            {
                Phase1Start = 0.0,
                Phase1End = 5.0,
                Phase2Start = 5.0,
                Phase2End = 40.0,
                Phase3Start = 40.0,
                Phase3End = _runtime
            },

            MonitoredProcess = new MonitoredProcess
            {
                Name = _processName,
                Pid = 12345
            },

            System = _systemInfo ?? SystemInfoBuilder.CreateDefault(),

            Configuration = _configuration ?? new TestConfiguration
            {
                NumEvents = 10000,
                ApiDuration = "30s",
                ApiConcurrentWorkers = 1,
                RabbitmqExchange = "referenceservice.comparison",
                ConsumerQueue = "service-tester",
                ApiEndpoint = $"http://localhost:8099/kpi",
                PublishEventType = "instrument.status.changed",
                ConsumeEventType = "instrumentstatus.kpi.updated"
            },

            Results = _results ?? new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = 5.0,
                    ThroughputEventsPerSec = 2000.0
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = 35.0,
                    ThroughputEventsPerSec = _eventsThroughput
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = _runtime - 40.0,
                    TotalRequests = 3000,
                    ThroughputCallsPerSec = _apiThroughput,
                    SuccessCount = 3000,
                    SuccessPercentage = 100.0,
                    ErrorCount = 0,
                    ErrorPercentage = 0.0
                }
            },

            EventsThroughputSamples = eventsSamples,
            ApiThroughputSamples = apiSamples,
            ProcessResourceSamples = processSamples,
            SystemResourceSamples = systemSamples,
            RabbitMqResourceSamples = rabbitmqSamples,
            PostgresResourceSamples = postgresSamples
        };
    }
}

/// <summary>
/// Builder for throughput samples with statistical properties.
/// </summary>
internal static class ThroughputSampleBuilder
{
    public static IReadOnlyList<ThroughputSample> Create(
        DateTime baseTime,
        double targetAverage,
        double targetCv)
    {
        var random = new Random(42);
        var rawValues = new List<double>();
        var variance = targetAverage * (targetCv / 100.0);

        for (int i = 0; i < 10; i++)
        {
            var value = targetAverage + ((random.NextDouble() - 0.5) * 2 * variance);
            rawValues.Add(Math.Max(0, value));
        }

        var currentAvg = rawValues.Average();
        var adjustment = targetAverage - currentAvg;

        var samples = new List<ThroughputSample>();
        for (int i = 0; i < rawValues.Count; i++)
        {
            samples.Add(new ThroughputSample
            {
                Timestamp = baseTime.AddSeconds(i * 0.1),
                ElapsedSeconds = i * 0.1,
                Rate = Math.Max(0, rawValues[i] + adjustment),
                CumulativeCount = (i + 1) * 100
            });
        }

        return samples;
    }

    /// <summary>
    /// Creates simple throughput samples with linear progression for deterministic testing.
    /// </summary>
    public static IReadOnlyList<ThroughputSample> CreateSimple(
        DateTime baseTime,
        int count,
        double startRate = 100.0,
        double rateIncrement = 10.0)
    {
        var samples = new List<ThroughputSample>();
        for (int i = 0; i < count; i++)
        {
            samples.Add(new ThroughputSample
            {
                Timestamp = baseTime.AddSeconds(i * 0.1),
                ElapsedSeconds = i * 0.1,
                Rate = startRate + (i * rateIncrement),
                CumulativeCount = (i + 1) * 10
            });
        }
        return samples;
    }

    /// <summary>
    /// Creates throughput samples with known statistics for testing calculations.
    /// Average: 100.00, Peak: 150.00, Min: 50.00
    /// </summary>
    public static IReadOnlyList<ThroughputSample> CreateWithKnownStatistics(DateTime baseTime)
    {
        return new List<ThroughputSample>
        {
            new() { Timestamp = baseTime, ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = baseTime.AddSeconds(0.1), ElapsedSeconds = 0.2, Rate = 150.0, CumulativeCount = 25 },
            new() { Timestamp = baseTime.AddSeconds(0.2), ElapsedSeconds = 0.3, Rate = 75.0, CumulativeCount = 32 },
            new() { Timestamp = baseTime.AddSeconds(0.3), ElapsedSeconds = 0.4, Rate = 50.0, CumulativeCount = 37 },
            new() { Timestamp = baseTime.AddSeconds(0.4), ElapsedSeconds = 0.5, Rate = 125.0, CumulativeCount = 50 }
        };
    }

    /// <summary>
    /// Creates throughput samples with zeros interspersed for testing min calculation.
    /// Min should exclude zeros and return 50.0
    /// </summary>
    public static IReadOnlyList<ThroughputSample> CreateWithZeros(DateTime baseTime)
    {
        return new List<ThroughputSample>
        {
            new() { Timestamp = baseTime, ElapsedSeconds = 0.1, Rate = 100.0, CumulativeCount = 10 },
            new() { Timestamp = baseTime.AddSeconds(0.1), ElapsedSeconds = 0.2, Rate = 150.0, CumulativeCount = 25 },
            new() { Timestamp = baseTime.AddSeconds(0.2), ElapsedSeconds = 0.3, Rate = 0.0, CumulativeCount = 25 },
            new() { Timestamp = baseTime.AddSeconds(0.3), ElapsedSeconds = 0.4, Rate = 75.0, CumulativeCount = 32 },
            new() { Timestamp = baseTime.AddSeconds(0.4), ElapsedSeconds = 0.5, Rate = 0.0, CumulativeCount = 32 },
            new() { Timestamp = baseTime.AddSeconds(0.5), ElapsedSeconds = 0.6, Rate = 50.0, CumulativeCount = 37 }
        };
    }
}

/// <summary>
/// Builder for resource usage samples.
/// </summary>
internal static class ResourceSampleBuilder
{
    public static IReadOnlyList<ProcessResourceSample> CreateProcess(
        DateTime baseTime,
        double targetAvgCpu,
        double targetAvgMemory)
    {
        var rawCpuValues = new List<double>();
        var rawMemoryValues = new List<double>();

        for (int i = 0; i < 10; i++)
        {
            var cpuVariance = (i % 3 - 1) * 2.0;
            var memoryVariance = (i % 3 - 1) * 10.0;

            rawCpuValues.Add(targetAvgCpu + cpuVariance);
            rawMemoryValues.Add(targetAvgMemory + memoryVariance);
        }

        var cpuAdjustment = targetAvgCpu - rawCpuValues.Average();
        var memoryAdjustment = targetAvgMemory - rawMemoryValues.Average();

        var samples = new List<ProcessResourceSample>();
        for (int i = 0; i < rawCpuValues.Count; i++)
        {
            samples.Add(new ProcessResourceSample
            {
                Timestamp = baseTime.AddSeconds(i * 0.5),
                ElapsedSeconds = i * 0.5,
                CpuPercent = rawCpuValues[i] + cpuAdjustment,
                MemoryRssMb = rawMemoryValues[i] + memoryAdjustment,
                Threads = 5 + i
            });
        }

        return samples;
    }

    /// <summary>
    /// Creates simple process resource samples with linear progression for deterministic testing.
    /// </summary>
    public static IReadOnlyList<ProcessResourceSample> CreateProcessSimple(
        DateTime baseTime,
        int count,
        double startCpu = 25.0,
        double cpuIncrement = 2.0,
        double startMemory = 100.0,
        double memoryIncrement = 5.0)
    {
        var samples = new List<ProcessResourceSample>();
        for (int i = 0; i < count; i++)
        {
            samples.Add(new ProcessResourceSample
            {
                Timestamp = baseTime.AddSeconds(i * 0.5),
                ElapsedSeconds = i * 0.5,
                CpuPercent = startCpu + (i * cpuIncrement),
                MemoryRssMb = startMemory + (i * memoryIncrement),
                Threads = 5 + i
            });
        }
        return samples;
    }

    public static IReadOnlyList<ContainerResourceSample> CreateContainer(DateTime baseTime, int count)
    {
        var samples = new List<ContainerResourceSample>();

        for (int i = 0; i < count; i++)
        {
            samples.Add(new ContainerResourceSample
            {
                Timestamp = baseTime.AddSeconds(i * 3.0),
                ElapsedSeconds = i * 3.0,
                CpuPercent = 15.0 + (i * 1.0),
                MemoryMb = 200.0 + (i * 10.0)
            });
        }

        return samples;
    }

    /// <summary>
    /// Creates simple container resource samples with linear progression for deterministic testing.
    /// </summary>
    public static IReadOnlyList<ContainerResourceSample> CreateContainerSimple(
        DateTime baseTime,
        int count,
        double startCpu = 15.0,
        double cpuIncrement = 1.0,
        double startMemory = 200.0,
        double memoryIncrement = 10.0)
    {
        var samples = new List<ContainerResourceSample>();
        for (int i = 0; i < count; i++)
        {
            samples.Add(new ContainerResourceSample
            {
                Timestamp = baseTime.AddSeconds(i * 3.0),
                ElapsedSeconds = i * 3.0,
                CpuPercent = startCpu + (i * cpuIncrement),
                MemoryMb = startMemory + (i * memoryIncrement)
            });
        }
        return samples;
    }
}

/// <summary>
/// Builder for system information objects.
/// </summary>
internal static class SystemInfoBuilder
{
    /// <summary>
    /// Creates SystemInfo with Intel i9-13900K specifications.
    /// Matches the hardware specs used in original test helper methods.
    /// </summary>
    public static SystemInfo CreateIntelI9()
    {
        return new SystemInfo
        {
            Os = "Linux",
            OsRelease = "6.6.87.2-microsoft-standard-WSL2",
            OsVersion = "#1 SMP Wed Aug 14 21:50:04 UTC 2024",
            WslVersion = "WSL2",
            Cpu = new CpuInfo
            {
                Model = "Intel(R) Core(TM) i9-13900K CPU @ 3.00GHz",
                LogicalProcessors = 8,
                PhysicalProcessors = 4,
                SpeedMhz = 4200.00
            },
            Ram = new RamInfo
            {
                TotalGb = 32.00,
                Speed = "3600 MHz",
                Type = "DDR4",
                Manufacturer = "SK Hynix"
            },
            Disks = new List<DiskInfo>
            {
                new DiskInfo
                {
                    Name = "Samsung 980 Pro",
                    Size = "2.0T",
                    Type = "SSD (NVMe)",
                    Model = "Samsung 980 Pro"
                }
            }
        };
    }

    /// <summary>
    /// Creates SystemInfo with AMD Ryzen 9 7950X specifications.
    /// </summary>
    public static SystemInfo CreateDefault()
    {
        return new SystemInfo
        {
            Os = "Linux",
            OsRelease = "6.6.87.2-microsoft-standard-WSL2",
            OsVersion = "#1 SMP Wed Aug 14 21:50:04 UTC 2024",
            WslVersion = "WSL2",
            Cpu = new CpuInfo
            {
                Model = "AMD Ryzen 9 7950X 16-Core Processor",
                LogicalProcessors = 32,
                PhysicalProcessors = 1,
                SpeedMhz = 4500.0
            },
            Ram = new RamInfo
            {
                TotalGb = 64.00,
                Speed = "5200 MHz",
                Type = "DDR5",
                Manufacturer = "G.Skill"
            },
            Disks = new List<DiskInfo>
            {
                new DiskInfo
                {
                    Name = "Samsung 990 PRO",
                    Size = "2.0T",
                    Type = "SSD (NVMe)",
                    Model = "Samsung 990 PRO 2TB"
                }
            }
        };
    }
}
