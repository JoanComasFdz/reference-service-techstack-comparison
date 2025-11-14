using ScottPlot;

namespace PerformanceTester.Reporting.ChartGeneration;

/// <summary>
/// Defines the exact color palette used for performance metrics charts.
/// Colors match the Python matplotlib implementation for visual consistency.
/// </summary>
public static class ChartColors
{
    // Throughput Colors (Subplot 1)

    /// <summary>
    /// Events throughput primary line color: #2ecc71 (green)
    /// </summary>
    public static readonly Color EventsPrimary = Color.FromHex("#2ecc71");

    /// <summary>
    /// Events throughput average line color: #27ae60 (darker green)
    /// </summary>
    public static readonly Color EventsAverage = Color.FromHex("#27ae60");

    /// <summary>
    /// API throughput primary line color: #e67e22 (orange)
    /// </summary>
    public static readonly Color ApiPrimary = Color.FromHex("#e67e22");

    /// <summary>
    /// API throughput average line color: #d35400 (darker orange)
    /// </summary>
    public static readonly Color ApiAverage = Color.FromHex("#d35400");

    // Service Resource Colors (Subplot 2)

    /// <summary>
    /// Service CPU% primary line color: #3498db (blue)
    /// </summary>
    public static readonly Color ServiceCpu = Color.FromHex("#3498db");

    /// <summary>
    /// Service CPU% average line color: #2980b9 (darker blue)
    /// </summary>
    public static readonly Color ServiceCpuAvg = Color.FromHex("#2980b9");

    /// <summary>
    /// Service RAM MB primary line color: #e74c3c (red)
    /// </summary>
    public static readonly Color ServiceRam = Color.FromHex("#e74c3c");

    /// <summary>
    /// Service RAM MB average line color: #c0392b (darker red)
    /// </summary>
    public static readonly Color ServiceRamAvg = Color.FromHex("#c0392b");

    // RabbitMQ Resource Colors (Subplot 3)

    /// <summary>
    /// RabbitMQ CPU% primary line color: #9b59b6 (purple)
    /// </summary>
    public static readonly Color RabbitMqCpu = Color.FromHex("#9b59b6");

    /// <summary>
    /// RabbitMQ CPU% average line color: #8e44ad (darker purple)
    /// </summary>
    public static readonly Color RabbitMqCpuAvg = Color.FromHex("#8e44ad");

    /// <summary>
    /// RabbitMQ RAM MB primary line color: #e67e22 (orange)
    /// </summary>
    public static readonly Color RabbitMqRam = Color.FromHex("#e67e22");

    /// <summary>
    /// RabbitMQ RAM MB average line color: #d35400 (darker orange)
    /// </summary>
    public static readonly Color RabbitMqRamAvg = Color.FromHex("#d35400");

    // PostgreSQL Resource Colors (Subplot 4)

    /// <summary>
    /// PostgreSQL CPU% primary line color: #16a085 (teal)
    /// </summary>
    public static readonly Color PostgresCpu = Color.FromHex("#16a085");

    /// <summary>
    /// PostgreSQL CPU% average line color: #138d75 (darker teal)
    /// </summary>
    public static readonly Color PostgresCpuAvg = Color.FromHex("#138d75");

    /// <summary>
    /// PostgreSQL RAM MB primary line color: #f39c12 (yellow/gold)
    /// </summary>
    public static readonly Color PostgresRam = Color.FromHex("#f39c12");

    /// <summary>
    /// PostgreSQL RAM MB average line color: #e67e22 (darker yellow/orange)
    /// </summary>
    public static readonly Color PostgresRamAvg = Color.FromHex("#e67e22");

    // System Resource Colors (Subplot 5)

    /// <summary>
    /// System CPU% primary line color: #34495e (dark gray)
    /// </summary>
    public static readonly Color SystemCpu = Color.FromHex("#34495e");

    /// <summary>
    /// System CPU% average line color: #2c3e50 (darker gray)
    /// </summary>
    public static readonly Color SystemCpuAvg = Color.FromHex("#2c3e50");

    /// <summary>
    /// System RAM MB primary line color: #c0392b (dark red)
    /// </summary>
    public static readonly Color SystemRam = Color.FromHex("#c0392b");

    /// <summary>
    /// System RAM MB average line color: #a93226 (darker red)
    /// </summary>
    public static readonly Color SystemRamAvg = Color.FromHex("#a93226");

    // Phase Boundary Colors

    /// <summary>
    /// Consume phase start boundary and label color: #27ae60 (green)
    /// </summary>
    public static readonly Color ConsumePhase = Color.FromHex("#27ae60");

    /// <summary>
    /// Consume phase end boundary color: #2980b9 (blue)
    /// </summary>
    public static readonly Color ConsumeBoundary = Color.FromHex("#2980b9");

    /// <summary>
    /// API phase start/end boundary and label color: #e67e22 (orange)
    /// </summary>
    public static readonly Color ApiPhase = Color.FromHex("#e67e22");
}
