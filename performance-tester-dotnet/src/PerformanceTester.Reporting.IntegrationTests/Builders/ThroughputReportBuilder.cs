using PerformanceTester.Reporting;

namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Fluent builder for creating ThroughputReport instances with samples and statistical summary.
/// Supports both statistical variation mode and linear progression mode.
/// </summary>
public class ThroughputReportBuilder
{
    private readonly DateTime _baseTime;
    private int _sampleCount = 200;
    private int _samplingIntervalMs = 100;

    // Statistical mode fields
    private double? _targetAverage;
    private double? _targetCv;

    // Linear progression mode fields
    private double? _startRate;
    private double? _rateIncrement;

    /// <summary>
    /// Creates a new ThroughputReportBuilder with the specified base timestamp.
    /// </summary>
    /// <param name="baseTime">Base timestamp for first sample</param>
    public ThroughputReportBuilder(DateTime baseTime)
    {
        _baseTime = baseTime;
    }

    /// <summary>
    /// Sets the number of samples to generate.
    /// </summary>
    /// <param name="count">Number of samples</param>
    public ThroughputReportBuilder WithSampleCount(int count)
    {
        _sampleCount = count;
        return this;
    }

    /// <summary>
    /// Sets the sampling interval in milliseconds.
    /// </summary>
    /// <param name="milliseconds">Sampling interval</param>
    public ThroughputReportBuilder WithSamplingInterval(int milliseconds)
    {
        _samplingIntervalMs = milliseconds;
        return this;
    }

    /// <summary>
    /// Configures statistical variation mode with target average and coefficient of variation.
    /// Mutually exclusive with linear progression mode.
    /// </summary>
    /// <param name="targetAverage">Target average throughput rate</param>
    /// <param name="targetCv">Target coefficient of variation (percentage)</param>
    public ThroughputReportBuilder WithStatisticalVariation(double targetAverage, double targetCv)
    {
        _targetAverage = targetAverage;
        _targetCv = targetCv;
        _startRate = null;
        _rateIncrement = null;
        return this;
    }

    /// <summary>
    /// Configures linear progression mode with deterministic rate increase.
    /// Mutually exclusive with statistical variation mode.
    /// </summary>
    /// <param name="startRate">Starting throughput rate</param>
    /// <param name="rateIncrement">Rate increment per sample</param>
    public ThroughputReportBuilder WithLinearProgression(double startRate, double rateIncrement)
    {
        _startRate = startRate;
        _rateIncrement = rateIncrement;
        _targetAverage = null;
        _targetCv = null;
        return this;
    }

    /// <summary>
    /// Builds the ThroughputReport with configured parameters.
    /// Uses statistical variation mode if configured, otherwise linear progression mode.
    /// </summary>
    /// <returns>Complete ThroughputReport with samples and summary</returns>
    /// <exception cref="InvalidOperationException">Thrown if neither mode is configured</exception>
    public ThroughputReport Build()
    {
        // Validate configuration first
        if (_targetAverage is null && _startRate is null)
        {
            throw new InvalidOperationException(
                "Must configure either statistical variation (WithStatisticalVariation) or linear progression (WithLinearProgression)");
        }

        return _targetAverage.HasValue
            ? BuildStatistical()
            : BuildLinear();
    }

    private ThroughputReport BuildStatistical()
    {
        var jsonSamples = new List<ThroughputSampleJson>();
        var random = new Random(42);
        var variance = _targetAverage!.Value * (_targetCv!.Value / 100.0);

        for (int i = 0; i < _sampleCount; i++)
        {
            var elapsed = i * (_samplingIntervalMs / 1000.0);
            var timestamp = _baseTime.AddSeconds(elapsed);
            var rate = _targetAverage.Value + ((random.NextDouble() - 0.5) * 2 * variance);
            rate = Math.Max(0, rate);

            jsonSamples.Add(new ThroughputSampleJson
            {
                Timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff+00:00"),
                ElapsedSeconds = Math.Round(elapsed, 3),
                EventsPerSecond = Math.Round(rate, 2),
                TotalEvents = (int)(rate * (i + 1))
            });
        }

        return CreateReportWithSummary(jsonSamples);
    }

    private ThroughputReport BuildLinear()
    {
        var jsonSamples = new List<ThroughputSampleJson>();

        for (int i = 0; i < _sampleCount; i++)
        {
            var elapsed = i * (_samplingIntervalMs / 1000.0);
            var timestamp = _baseTime.AddSeconds(elapsed);
            var rate = _startRate!.Value + (i * _rateIncrement!.Value);

            jsonSamples.Add(new ThroughputSampleJson
            {
                Timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff+00:00"),
                ElapsedSeconds = Math.Round(elapsed, 3),
                EventsPerSecond = Math.Round(rate, 2),
                TotalEvents = (i + 1) * 10
            });
        }

        return CreateReportWithSummary(jsonSamples);
    }

    private ThroughputReport CreateReportWithSummary(List<ThroughputSampleJson> jsonSamples)
    {
        // Calculate summary statistics
        var rates = jsonSamples.Select(s => s.ThroughputRate).ToList();
        var summary = new ThroughputSummary
        {
            AvgRate = Math.Round(rates.Average(), 2),
            PeakRate = Math.Round(rates.Max(), 2),
            MinRate = Math.Round(rates.Where(r => r > 0).Min(), 2),
            StdDevRate = Math.Round(StatisticsCalculator.CalculateStdDev(rates), 2),
            CvRate = Math.Round(StatisticsCalculator.CalculateCV(rates), 1),
            AvgResponseTimeMs = Math.Round(1000.0 / rates.Average(), 3),
            TotalSamples = _sampleCount,
            TotalCount = jsonSamples.Last().CumulativeCount
        };

        return new ThroughputReport
        {
            TestDate = _baseTime.ToString("yyyy-MM-dd HH:mm:ss"),
            SamplingIntervalMs = _samplingIntervalMs,
            Samples = jsonSamples,
            Summary = summary
        };
    }
}
