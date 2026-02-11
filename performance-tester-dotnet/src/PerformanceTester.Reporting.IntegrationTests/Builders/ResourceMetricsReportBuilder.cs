using PerformanceTester.Reporting;

namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Fluent builder for creating ResourceMetricsReport instances with samples and statistical summaries.
/// Supports both phase-based variation mode and linear progression mode for CPU and memory metrics.
/// </summary>
internal class ResourceMetricsReportBuilder
{
    private readonly DateTime _baseTime;
    private readonly string _resourceType;
    private int _sampleCount = 130;

    // Statistical mode fields
    private double? _targetAvgCpu;
    private double? _targetAvgMemory;

    // Linear progression mode fields
    private double? _startCpu;
    private double? _cpuIncrement;
    private double? _startMemory;
    private double? _memoryIncrement;

    /// <summary>
    /// Creates a new ResourceMetricsReportBuilder.
    /// </summary>
    /// <param name="baseTime">Base timestamp for first sample</param>
    /// <param name="resourceType">Resource type (service, system, rabbitmq, postgres) - affects sampling interval</param>
    public ResourceMetricsReportBuilder(DateTime baseTime, string resourceType)
    {
        _baseTime = baseTime;
        _resourceType = resourceType;
    }

    /// <summary>
    /// Sets the number of samples to generate.
    /// </summary>
    /// <param name="count">Number of samples</param>
    public ResourceMetricsReportBuilder WithSampleCount(int count)
    {
        _sampleCount = count;
        return this;
    }

    /// <summary>
    /// Configures phase-based variation mode with target averages.
    /// Generates realistic CPU/memory patterns with phase transitions and variance.
    /// Mutually exclusive with linear progression mode.
    /// </summary>
    /// <param name="targetAvgCpu">Target average CPU percentage</param>
    /// <param name="targetAvgMemory">Target average memory in MB</param>
    public ResourceMetricsReportBuilder WithPhaseBasedVariation(double targetAvgCpu, double targetAvgMemory)
    {
        _targetAvgCpu = targetAvgCpu;
        _targetAvgMemory = targetAvgMemory;
        _startCpu = null;
        _cpuIncrement = null;
        _startMemory = null;
        _memoryIncrement = null;
        return this;
    }

    /// <summary>
    /// Configures linear progression mode with deterministic increases.
    /// Useful for deterministic testing scenarios.
    /// Mutually exclusive with phase-based variation mode.
    /// </summary>
    /// <param name="startCpu">Starting CPU percentage</param>
    /// <param name="cpuIncrement">CPU increment per sample</param>
    /// <param name="startMemory">Starting memory in MB</param>
    /// <param name="memoryIncrement">Memory increment per sample</param>
    public ResourceMetricsReportBuilder WithLinearProgression(
        double startCpu,
        double cpuIncrement,
        double startMemory,
        double memoryIncrement)
    {
        _startCpu = startCpu;
        _cpuIncrement = cpuIncrement;
        _startMemory = startMemory;
        _memoryIncrement = memoryIncrement;
        _targetAvgCpu = null;
        _targetAvgMemory = null;
        return this;
    }

    /// <summary>
    /// Builds the ResourceMetricsReport with configured parameters.
    /// Uses phase-based variation mode if configured, otherwise linear progression mode.
    /// </summary>
    /// <returns>Complete ResourceMetricsReport with samples and summaries</returns>
    /// <exception cref="InvalidOperationException">Thrown if neither mode is configured</exception>
    public ResourceMetricsReport Build()
    {
        // Determine which mode to use
        if (_targetAvgCpu.HasValue && _targetAvgMemory.HasValue)
        {
            return BuildPhaseBasedVariation();
        }
        else if (_startCpu.HasValue && _cpuIncrement.HasValue && _startMemory.HasValue && _memoryIncrement.HasValue)
        {
            return BuildLinear();
        }
        else
        {
            throw new InvalidOperationException(
                "Must configure either phase-based variation (WithPhaseBasedVariation) or linear progression (WithLinearProgression)");
        }
    }

    private ResourceMetricsReport BuildPhaseBasedVariation()
    {
        // Determine sampling interval based on resource type (matches Python behavior)
        var samplingIntervalMs = (_resourceType == "service" || _resourceType == "system") ? 500 : 3000;
        var samplingIntervalSec = samplingIntervalMs / 1000.0;

        var jsonSamples = new List<ResourceSampleJson>();
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

            jsonSamples.Add(new ResourceSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = Math.Round(elapsed, 3),
                CpuPercent = Math.Round(cpu, 2),
                MemoryMb = Math.Round(ram, 2)
            });
        }

        return CreateReportWithSummaries(jsonSamples, samplingIntervalMs);
    }

    private ResourceMetricsReport BuildLinear()
    {
        // Default to service interval for linear mode
        var samplingIntervalMs = 500;
        var samplingIntervalSec = samplingIntervalMs / 1000.0;
        var jsonSamples = new List<ResourceSampleJson>();

        for (int i = 0; i < _sampleCount; i++)
        {
            var elapsed = i * samplingIntervalSec;
            var timestamp = _baseTime.AddSeconds(elapsed);

            jsonSamples.Add(new ResourceSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = Math.Round(elapsed, 3),
                CpuPercent = Math.Round(_startCpu!.Value + (i * _cpuIncrement!.Value), 2),
                MemoryMb = Math.Round(_startMemory!.Value + (i * _memoryIncrement!.Value), 2)
            });
        }

        return CreateReportWithSummaries(jsonSamples, samplingIntervalMs);
    }

    private ResourceMetricsReport CreateReportWithSummaries(List<ResourceSampleJson> jsonSamples, int samplingIntervalMs)
    {
        // Calculate CPU summary statistics
        var cpuValues = jsonSamples.Select(s => s.CpuPercent).ToList();
        var cpuSummary = new ResourceSummary
        {
            Avg = Math.Round(cpuValues.Average(), 2),
            Min = Math.Round(cpuValues.Min(), 2),
            Max = Math.Round(cpuValues.Max(), 2),
            Mode = (int)Math.Round(cpuValues.Average()),
            Unit = "%"
        };

        // Calculate memory summary statistics
        var ramValues = jsonSamples.Select(s => s.MemoryMb).ToList();
        var memorySummary = new ResourceSummary
        {
            Avg = Math.Round(ramValues.Average(), 2),
            Min = Math.Round(ramValues.Min(), 2),
            Max = Math.Round(ramValues.Max(), 2),
            Mode = (int)Math.Round(ramValues.Average()),
            Unit = "MB"
        };

        return new ResourceMetricsReport
        {
            TestDate = _baseTime,
            SamplingIntervalMs = samplingIntervalMs,
            Samples = jsonSamples,
            CpuSummary = cpuSummary,
            MemorySummary = memorySummary
        };
    }
}
