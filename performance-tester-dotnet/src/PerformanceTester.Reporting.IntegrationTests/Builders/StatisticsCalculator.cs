namespace PerformanceTester.Reporting.IntegrationTests.Builders;

/// <summary>
/// Utility class for calculating statistical metrics from data samples.
/// Used in tests to verify calculated statistics match expected values.
/// </summary>
public static class StatisticsCalculator
{
    /// <summary>
    /// Calculates the sample standard deviation of a collection of values.
    /// Uses the sample standard deviation formula (N-1 degrees of freedom).
    /// </summary>
    /// <param name="values">The values to calculate standard deviation for</param>
    /// <returns>The sample standard deviation, or 0.0 if fewer than 2 values</returns>
    public static double CalculateStdDev(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count < 2) return 0.0;

        var avg = valuesList.Average();
        var sumOfSquares = valuesList.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / (valuesList.Count - 1));
    }

    /// <summary>
    /// Calculates the coefficient of variation (CV) as a percentage.
    /// CV = (Standard Deviation / Mean) × 100%
    /// Measures relative variability - lower values indicate more consistent data.
    /// </summary>
    /// <param name="values">The values to calculate CV for</param>
    /// <returns>The coefficient of variation as a percentage, or 0.0 if fewer than 2 values or mean is 0</returns>
    public static double CalculateCV(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count < 2) return 0.0;

        var avg = valuesList.Average();
        if (avg == 0.0) return 0.0;

        var stdDev = CalculateStdDev(valuesList);
        return (stdDev / avg) * 100.0;
    }
}
