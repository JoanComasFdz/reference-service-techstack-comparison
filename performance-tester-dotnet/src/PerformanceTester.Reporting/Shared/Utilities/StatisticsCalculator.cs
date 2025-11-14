namespace PerformanceTester.Reporting.Shared.Utilities;

/// <summary>
/// Provides statistical calculations matching Python implementation.
/// Uses MathNet.Numerics for mathematical operations.
/// </summary>
/// <remarks>
/// IMPORTANT: All formulas must match Python behavior exactly:
/// - Standard deviation: Sample (N-1), not population (N)
/// - Min value filtering: Excludes zeros for min, includes in average
/// - Mode: Most common rounded integer value
/// - CV%: (stdDev / mean) * 100 as percentage
/// </remarks>
public static class StatisticsCalculator
{
    /// <summary>
    /// Calculates average (mean) of values.
    /// Returns 0.0 if collection is empty.
    /// </summary>
    public static double CalculateAverage(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0) return 0.0;

        return valuesList.Average();
    }

    /// <summary>
    /// Calculates sample standard deviation (N-1 denominator).
    /// Returns 0.0 if fewer than 2 values.
    /// Uses MathNet.Numerics.Statistics.StandardDeviation (defaults to sample).
    /// </summary>
    /// <remarks>
    /// Matches Python's statistics.stdev() which uses Bessel's correction (N-1).
    /// </remarks>
    public static double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count < 2) return 0.0;

        return Statistics.StandardDeviation(valuesList);
    }

    /// <summary>
    /// Calculates coefficient of variation as percentage.
    /// Formula: (standardDeviation / mean) * 100.
    /// Returns 0.0 if mean is zero (division by zero).
    /// </summary>
    /// <remarks>
    /// Lower CV% indicates more stable, consistent performance.
    /// Typical interpretation:
    /// - CV% &lt; 10%: Low variability (very stable)
    /// - CV% 10-20%: Moderate variability
    /// - CV% &gt; 20%: High variability (unstable)
    /// </remarks>
    public static double CalculateCoefficientOfVariation(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count < 2) return 0.0;

        var mean = valuesList.Average();
        if (mean == 0.0) return 0.0;

        var stdDev = Statistics.StandardDeviation(valuesList);
        return (stdDev / mean) * 100.0;
    }

    /// <summary>
    /// Calculates mode (most frequently occurring value).
    /// Values are rounded to nearest integer before frequency analysis.
    /// Returns 0 if collection is empty.
    /// </summary>
    /// <remarks>
    /// Matches Python implementation:
    /// 1. Round each value to nearest integer
    /// 2. Count frequency of each rounded value
    /// 3. Return most common value
    /// </remarks>
    public static int CalculateMode(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0) return 0;

        // Round to nearest integer and count frequency
        var frequencies = valuesList
            .Select(v => (int)Math.Round(v))
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key) // Tie-breaker: prefer higher value
            .FirstOrDefault();

        return frequencies?.Key ?? 0;
    }

    /// <summary>
    /// Calculates minimum value, excluding zeros.
    /// Returns 0.0 if no non-zero values exist.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: Python excludes zeros from min calculation but includes them in average.
    /// This matches Python's filtering behavior:
    /// <code>
    /// non_zero_rates = [r for r in rate_values if r &gt; 0]
    /// min_rate = min(non_zero_rates) if non_zero_rates else 0
    /// </code>
    /// </remarks>
    public static double CalculateMinExcludingZeros(IEnumerable<double> values)
    {
        var nonZeroValues = values.Where(v => v > 0).ToList();
        if (nonZeroValues.Count == 0) return 0.0;

        return nonZeroValues.Min();
    }

    /// <summary>
    /// Calculates maximum value.
    /// Returns 0.0 if collection is empty.
    /// </summary>
    public static double CalculateMax(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0) return 0.0;

        return valuesList.Max();
    }

    /// <summary>
    /// Calculates Nth percentile using linear interpolation.
    /// </summary>
    /// <param name="values">Collection of values.</param>
    /// <param name="percentile">Percentile to calculate (0-100).</param>
    /// <returns>Percentile value, or 0.0 if collection is empty.</returns>
    /// <remarks>
    /// Uses linear interpolation between ranks (matches NumPy default).
    /// Uses MathNet.Numerics.Statistics.Percentile.
    /// </remarks>
    public static double CalculatePercentile(IEnumerable<double> values, double percentile)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0) return 0.0;

        return Statistics.Percentile(valuesList, (int)percentile);
    }

    /// <summary>
    /// Calculates average response time in milliseconds from throughput rate.
    /// Formula: 1000.0 / averageRate.
    /// Returns 0.0 if average rate is zero.
    /// </summary>
    /// <remarks>
    /// Response time is the inverse of throughput:
    /// - If throughput is 1000 events/sec, response time is 1.0 ms
    /// - If throughput is 500 events/sec, response time is 2.0 ms
    /// </remarks>
    public static double CalculateAverageResponseTime(double averageRate)
    {
        if (averageRate == 0.0) return 0.0;
        return 1000.0 / averageRate;
    }

    /// <summary>
    /// Calculates throughput summary statistics for a collection of samples.
    /// </summary>
    /// <param name="samples">Throughput samples (events or API calls).</param>
    /// <param name="rateSelector">Function to extract rate from sample.</param>
    /// <param name="countSelector">Function to extract cumulative count from sample.</param>
    /// <returns>Throughput summary with all statistical metrics.</returns>
    /// <remarks>
    /// IMPORTANT: Min calculation excludes zeros, but average includes all values.
    /// This matches Python's calculate_throughput_summary() function.
    /// </remarks>
    public static ThroughputSummary CalculateThroughputSummary<T>(
        IEnumerable<T> samples,
        Func<T, double> rateSelector,
        Func<T, int> countSelector)
    {
        var samplesList = samples.ToList();
        if (samplesList.Count == 0)
        {
            return new ThroughputSummary
            {
                AvgRate = 0.0,
                PeakRate = 0.0,
                MinRate = 0.0,
                StdDevRate = 0.0,
                CvRate = 0.0,
                AvgResponseTimeMs = 0.0,
                TotalSamples = 0,
                TotalCount = 0
            };
        }

        var rates = samplesList.Select(rateSelector).ToList();
        var avgRate = CalculateAverage(rates);
        var peakRate = CalculateMax(rates);
        var minRate = CalculateMinExcludingZeros(rates); // Excludes zeros
        var stdDevRate = CalculateStandardDeviation(rates);
        var cvRate = CalculateCoefficientOfVariation(rates);
        var avgResponseTimeMs = CalculateAverageResponseTime(avgRate);
        var totalCount = samplesList.Select(countSelector).LastOrDefault();

        return new ThroughputSummary
        {
            AvgRate = Math.Round(avgRate, 2),
            PeakRate = Math.Round(peakRate, 2),
            MinRate = Math.Round(minRate, 2),
            StdDevRate = Math.Round(stdDevRate, 2),
            CvRate = Math.Round(cvRate, 1), // 1 decimal place for CV%
            AvgResponseTimeMs = Math.Round(avgResponseTimeMs, 3),
            TotalSamples = samplesList.Count,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Calculates resource summary statistics (CPU or memory).
    /// </summary>
    /// <param name="values">Resource metric values.</param>
    /// <param name="unit">Unit of measurement (e.g., "%", "MB").</param>
    /// <returns>Resource summary with statistical metrics.</returns>
    public static ResourceSummary CalculateResourceSummary(IEnumerable<double> values, string unit)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0)
        {
            return new ResourceSummary
            {
                Avg = 0.0,
                Min = 0.0,
                Max = 0.0,
                Mode = 0,
                Unit = unit
            };
        }

        var avg = CalculateAverage(valuesList);
        var min = valuesList.Min();
        var max = valuesList.Max();
        var mode = CalculateMode(valuesList);

        return new ResourceSummary
        {
            Avg = Math.Round(avg, 2),
            Min = Math.Round(min, 2),
            Max = Math.Round(max, 2),
            Mode = mode,
            Unit = unit
        };
    }
}
