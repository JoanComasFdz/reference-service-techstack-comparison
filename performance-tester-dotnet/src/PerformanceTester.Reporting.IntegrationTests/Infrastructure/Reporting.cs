using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// Facade class that wraps IHost and exposes Reporting services for testing.
/// This class encapsulates all DI setup logic and provides easy access to production services.
/// </summary>
public sealed class Reporting : IDisposable
{
    private IHost? _host;

    /// <summary>
    /// System information detector for accessing hardware/OS information.
    /// </summary>
    public ISystemInfoDetector SystemInfoDetector { get; private set; } = null!;

    /// <summary>
    /// Report generator for creating JSON test reports.
    /// </summary>
    public IReportGenerator ReportGenerator { get; private set; } = null!;

    /// <summary>
    /// Chart generator for creating PNG visualization charts.
    /// </summary>
    public IChartGenerator ChartGenerator { get; private set; } = null!;

    /// <summary>
    /// Comparison report generator for creating comparison reports from multiple test results.
    /// </summary>
    public IComparisonReportGenerator ComparisonReportGenerator { get; private set; } = null!;

    public Reporting(ITestOutputHelper? output = null)
    {
        var builder = Host.CreateApplicationBuilder();

        // Clear default logging providers
        builder.Logging.ClearProviders();

        // Add logging with xUnit output if available
        if (output != null)
        {
            builder.Logging.AddXunitOutput(output);
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        // Register Reporting services (uses OSPlatformDetector internally)
        builder.Services.AddReporting();

        _host = builder.Build();

        // Resolve services
        SystemInfoDetector = _host.Services.GetRequiredService<ISystemInfoDetector>();
        ReportGenerator = _host.Services.GetRequiredService<IReportGenerator>();
        ChartGenerator = _host.Services.GetRequiredService<IChartGenerator>();
        ComparisonReportGenerator = _host.Services.GetRequiredService<IComparisonReportGenerator>();
    }

    public void Dispose()
    {
        _host?.Dispose();
        _host = null;
    }
}
