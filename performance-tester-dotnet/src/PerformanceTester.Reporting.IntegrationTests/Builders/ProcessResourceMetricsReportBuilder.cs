using PerformanceTester.Reporting;

namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Fluent builder for creating ProcessResourceMetricsReport instances with samples and statistical summaries.
/// Supports both phase-based variation mode and linear progression mode for CPU, RSS memory, and thread metrics.
/// </summary>
public class ProcessResourceMetricsReportBuilder
{
    private readonly DateTime _baseTime;
    private int _sampleCount = 130;

    // Statistical mode fields
    private double? _targetAvgCpu;
    private double? _targetAvgMemory;
    private int? _targetAvgThreads;

    // Linear progression mode fields
    private double? _startCpu;
    private double? _cpuIncrement;
    private double? _startMemory;
    private double? _memoryIncrement;
    private int? _startThreads;
    private int? _threadsIncrement;

    /// <summary>
    /// Creates a new ProcessResourceMetricsReportBuilder.
    /// </summary>
    /// <param name="baseTime">Base timestamp for first sample</param>
    public ProcessResourceMetricsReportBuilder(DateTime baseTime)
    {
        _baseTime = baseTime;
    }

    /// <summary>
    /// Sets the number of samples to generate.
    /// </summary>
    /// <param name="count">Number of samples</param>
    public ProcessResourceMetricsReportBuilder WithSampleCount(int count)
    {
        _sampleCount = count;
        return this;
    }

    /// <summary>
    /// Configures phase-based variation mode with target averages.
    /// Generates realistic CPU/memory/thread patterns with phase transitions and variance.
    /// Mutually exclusive with linear progression mode.
    /// </summary>
    /// <param name="targetAvgCpu">Target average CPU percentage</param>
    /// <param name="targetAvgMemory">Target average RSS memory in MB</param>
    /// <param name="targetAvgThreads">Target average thread count (default: 25)</param>
    public ProcessResourceMetricsReportBuilder WithPhaseBasedVariation(
        double targetAvgCpu,
        double targetAvgMemory,
        int targetAvgThreads = 25)
    {
        _targetAvgCpu = targetAvgCpu;
        _targetAvgMemory = targetAvgMemory;
        _targetAvgThreads = targetAvgThreads;
        _startCpu = null;
        _cpuIncrement = null;
        _startMemory = null;
        _memoryIncrement = null;
        _startThreads = null;
        _threadsIncrement = null;
        return this;
    }

    /// <summary>
    /// Configures linear progression mode with deterministic increases.
    /// Useful for deterministic testing scenarios.
    /// Mutually exclusive with phase-based variation mode.
    /// </summary>
    /// <param name="startCpu">Starting CPU percentage</param>
    /// <param name="cpuIncrement">CPU increment per sample</param>
    /// <param name="startMemory">Starting RSS memory in MB</param>
    /// <param name="memoryIncrement">Memory increment per sample</param>
    /// <param name="startThreads">Starting thread count</param>
    /// <param name="threadsIncrement">Thread count increment per sample</param>
    public ProcessResourceMetricsReportBuilder WithLinearProgression(
        double startCpu,
        double cpuIncrement,
        double startMemory,
        double memoryIncrement,
        int startThreads,
        int threadsIncrement)
    {
        _startCpu = startCpu;
        _cpuIncrement = cpuIncrement;
        _startMemory = startMemory;
        _memoryIncrement = memoryIncrement;
        _startThreads = startThreads;
        _threadsIncrement = threadsIncrement;
        _targetAvgCpu = null;
        _targetAvgMemory = null;
        _targetAvgThreads = null;
        return this;
    }

    /// <summary>
    /// Builds the ProcessResourceMetricsReport with configured parameters.
    /// Uses phase-based variation mode if configured, otherwise linear progression mode.
    /// </summary>
    /// <returns>Complete ProcessResourceMetricsReport with samples and summaries</returns>
    /// <exception cref="InvalidOperationException">Thrown if neither mode is configured</exception>
    public ProcessResourceMetricsReport Build()
    {
        // Determine which mode to use
        if (_targetAvgCpu.HasValue && _targetAvgMemory.HasValue)
        {
            return BuildPhaseBasedVariation();
        }
        else if (_startCpu.HasValue && _cpuIncrement.HasValue &&
                 _startMemory.HasValue && _memoryIncrement.HasValue &&
                 _startThreads.HasValue && _threadsIncrement.HasValue)
        {
            return BuildLinear();
        }
        else
        {
            throw new InvalidOperationException(
                "Must configure either phase-based variation (WithPhaseBasedVariation) or linear progression (WithLinearProgression)");
        }
    }

    private ProcessResourceMetricsReport BuildPhaseBasedVariation()
    {
        // Process resource metrics use 500ms sampling interval
        var samplingIntervalMs = 500;
        var samplingIntervalSec = samplingIntervalMs / 1000.0;

        var samples = new List<ProcessResourceSample>();
        var random = new Random(42);

        for (int i = 0; i < _sampleCount; i++)
        {
            var elapsed = i * samplingIntervalSec;
            var timestamp = _baseTime.AddSeconds(elapsed);

            // Phase-based CPU (consume phase: higher CPU, API phase: lower CPU)
            var cpuBase = elapsed < 35.5 ? _targetAvgCpu!.Value : _targetAvgCpu!.Value * 0.56;  // ~45% -> ~25%
            var cpu = cpuBase + random.Next(-5, 5);

            // Memory grows over time with some variance
            var ram = _targetAvgMemory!.Value + (elapsed * 0.5) + random.Next(-2, 2);

            // Threads vary slightly around target
            var threads = _targetAvgThreads ?? 25;
            threads += random.Next(-2, 3);

            samples.Add(new ProcessResourceSample
            {
                Timestamp = timestamp,
                ElapsedSeconds = Math.Round(elapsed, 3),
                CpuPercent = Math.Round(cpu, 2),
                MemoryRssMb = Math.Round(ram, 2),
                Threads = threads
            });
        }

        return CreateReportWithSummaries(samples, samplingIntervalMs);
    }

    private ProcessResourceMetricsReport BuildLinear()
    {
        // Process resource metrics use 500ms sampling interval
        var samplingIntervalMs = 500;
        var samplingIntervalSec = samplingIntervalMs / 1000.0;
        var samples = new List<ProcessResourceSample>();

        for (int i = 0; i < _sampleCount; i++)
        {
            var elapsed = i * samplingIntervalSec;
            var timestamp = _baseTime.AddSeconds(elapsed);

            samples.Add(new ProcessResourceSample
            {
                Timestamp = timestamp,
                ElapsedSeconds = Math.Round(elapsed, 3),
                CpuPercent = Math.Round(_startCpu!.Value + (i * _cpuIncrement!.Value), 2),
                MemoryRssMb = Math.Round(_startMemory!.Value + (i * _memoryIncrement!.Value), 2),
                Threads = _startThreads!.Value + (i * _threadsIncrement!.Value)
            });
        }

        return CreateReportWithSummaries(samples, samplingIntervalMs);
    }

    private ProcessResourceMetricsReport CreateReportWithSummaries(
        List<ProcessResourceSample> samples,
        int samplingIntervalMs)
    {
        // Calculate CPU summary statistics
        var cpuValues = samples.Select(s => s.CpuPercent).ToList();
        var cpuSummary = new ResourceSummary
        {
            Avg = Math.Round(cpuValues.Average(), 2),
            Min = Math.Round(cpuValues.Min(), 2),
            Max = Math.Round(cpuValues.Max(), 2),
            Mode = (int)Math.Round(cpuValues.Average()),
            Unit = "%"
        };

        // Calculate memory summary statistics
        var ramValues = samples.Select(s => s.MemoryRssMb).ToList();
        var memorySummary = new ResourceSummary
        {
            Avg = Math.Round(ramValues.Average(), 2),
            Min = Math.Round(ramValues.Min(), 2),
            Max = Math.Round(ramValues.Max(), 2),
            Mode = (int)Math.Round(ramValues.Average()),
            Unit = "MB"
        };

        return new ProcessResourceMetricsReport
        {
            TestDate = _baseTime,
            SamplingIntervalMs = samplingIntervalMs,
            Samples = samples,
            CpuSummary = cpuSummary,
            MemorySummary = memorySummary
        };
    }
}
