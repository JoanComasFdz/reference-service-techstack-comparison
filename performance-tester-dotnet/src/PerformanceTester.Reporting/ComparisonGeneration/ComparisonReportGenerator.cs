using System.Text;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.ComparisonGeneration;

/// <summary>
/// Default implementation of comparison report generator.
/// Generates Markdown comparison reports matching Python compare_test_results.py.
/// </summary>
public sealed class ComparisonReportGenerator
{
    private enum SortDirection
    {
        /// <summary>Higher values are better (descending sort, highest gets 🥇)</summary>
        HigherIsBetter,

        /// <summary>Lower values are better (ascending sort, lowest gets 🥇)</summary>
        LowerIsBetter
    }

    /// <summary>
    /// Generates a Markdown comparison report from multiple test reports.
    /// </summary>
    /// <param name="outputPath">Full path to output Markdown file.</param>
    /// <param name="testReports">Collection of test reports to compare.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    public async Task GenerateComparisonReportAsync(
        string outputPath,
        IEnumerable<TestReport> testReports,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));
        }

        var reports = testReports?.ToList() ?? throw new ArgumentNullException(nameof(testReports));

        if (reports.Count == 0)
        {
            throw new ArgumentException("Test reports collection cannot be empty.", nameof(testReports));
        }

        // Build Markdown content
        var markdown = new StringBuilder();

        // Header
        WriteHeader(markdown);

        // Test Environment
        WriteTestEnvironment(markdown, reports);

        // Test Runs Overview (sorted by runtime)
        WriteTestRunsOverview(markdown, reports);

        // Throughput Comparison
        markdown.AppendLine("## Throughput Comparison");
        markdown.AppendLine();

        WriteEventThroughputTable(markdown, reports);
        WriteApiThroughputTable(markdown, reports);

        // Resource Usage Comparison
        markdown.AppendLine("## Resource Usage Comparison");
        markdown.AppendLine();

        WriteCpuUsageTable(markdown, reports);
        WriteMemoryUsageTable(markdown, reports);
        WriteSystemMetricsTable(markdown, reports);

        // Performance Highlights
        WritePerformanceHighlights(markdown, reports);

        // Write to file
        await File.WriteAllTextAsync(outputPath, markdown.ToString(), cancellationToken);
    }

    private static void WriteHeader(StringBuilder markdown)
    {
        markdown.AppendLine("# Test Results Comparison Report");
        markdown.AppendLine();
        markdown.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        markdown.AppendLine();
    }

    private static void WriteTestEnvironment(StringBuilder markdown, List<TestReport> reports)
    {
        // Get system info from any report (should all be the same)
        var systemInfo = reports.First().System;
        var isWsl = !string.IsNullOrEmpty(systemInfo.WslVersion);

        // Calculate test run time information
        var timestamps = reports.Select(r => r.TestDate).OrderBy(d => d).ToList();
        var startTime = timestamps.First();
        var endTime = timestamps.Last();
        var totalDuration = (endTime - startTime).TotalSeconds;

        markdown.AppendLine("## Test Environment");
        markdown.AppendLine();
        markdown.AppendLine("**Hardware & System:**");

        // CPU and Memory
        markdown.AppendLine($"- **CPU Model:** {systemInfo.Cpu.Model}");
        markdown.AppendLine($"- **CPU Cores:** {systemInfo.Cpu.LogicalProcessors} cores");

        // Build memory description
        var memoryDesc = $"{systemInfo.Ram.TotalGb:F2} GB";
        if (!string.IsNullOrEmpty(systemInfo.Ram.Type))
        {
            memoryDesc += $" {systemInfo.Ram.Type}";
        }
        if (!string.IsNullOrEmpty(systemInfo.Ram.Speed))
        {
            memoryDesc += $" @ {systemInfo.Ram.Speed}";
        }
        markdown.AppendLine($"- **Total Memory:** {memoryDesc}");

        if (!string.IsNullOrEmpty(systemInfo.Ram.Manufacturer))
        {
            markdown.AppendLine($"- **Memory Manufacturer:** {systemInfo.Ram.Manufacturer}");
        }

        markdown.AppendLine($"- **Platform:** {systemInfo.Os} {systemInfo.OsRelease}".Trim());
        markdown.AppendLine($"- **.NET Version:** {Environment.Version}");
        markdown.AppendLine();

        // Disk information
        if (systemInfo.Disks.Any())
        {
            var driveCountDesc = isWsl
                ? $"{systemInfo.Disks.Count} physical drive(s) on Windows host"
                : $"{systemInfo.Disks.Count} drive(s)";

            markdown.AppendLine($"**Storage ({driveCountDesc}):**");
            markdown.AppendLine();

            for (int i = 0; i < systemInfo.Disks.Count; i++)
            {
                var disk = systemInfo.Disks[i];
                markdown.AppendLine($"**Drive {i + 1}:** {disk.Name}");
                markdown.AppendLine($"- **Type:** {disk.Type}");
                if (!string.IsNullOrEmpty(disk.Model))
                {
                    markdown.AppendLine($"- **Model:** {disk.Model}");
                }
                markdown.AppendLine($"- **Capacity:** {disk.Size}");
                markdown.AppendLine();
            }
        }
        else
        {
            markdown.AppendLine("**Storage:** Unable to retrieve disk information");
            markdown.AppendLine();
        }

        // Test Run Information
        markdown.AppendLine("**Test Run Information:**");
        markdown.AppendLine($"- **Start Time:** {startTime:yyyy-MM-dd HH:mm:ss}");
        markdown.AppendLine($"- **End Time:** {endTime:yyyy-MM-dd HH:mm:ss}");
        markdown.AppendLine($"- **Total Duration:** {FormatDuration(totalDuration)}");
        markdown.AppendLine($"- **Number of Services Tested:** {reports.Count}");
        markdown.AppendLine();
    }

    private static void WriteTestRunsOverview(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("## Test Runs Overview (sorted by Runtime - lower is better)");
        markdown.AppendLine();

        // Prepare data and sort by runtime
        var runtimeData = reports
            .Select(r => new
            {
                Report = r,
                Runtime = r.TotalRuntimeSeconds
            })
            .OrderBy(d => d.Runtime)
            .ToList();

        // Get top 3 runtimes (lower is better)
        var (first, second, third) = GetTop3Values(runtimeData, d => d.Runtime, SortDirection.LowerIsBetter);

        // Table header
        markdown.AppendLine("| # | Test Date | Process | Runtime (s) | Events | API Duration | Workers |");
        markdown.AppendLine("|---|-----------|---------|-------------|--------|--------------|---------|");

        // Table rows
        for (int i = 0; i < runtimeData.Count; i++)
        {
            var data = runtimeData[i];
            var report = data.Report;

            var runtimeStr = FormatNumber(data.Runtime, 2);
            runtimeStr = AddMedal(runtimeStr, data.Runtime, first, second, third);

            markdown.AppendLine(
                $"| {i + 1} | {report.TestDate:yyyy-MM-dd HH:mm:ss} | " +
                $"{report.MonitoredProcess.Name} | {runtimeStr} | " +
                $"{report.Configuration.NumEvents} | {report.Configuration.ApiDuration} | " +
                $"{report.Configuration.ApiConcurrentWorkers} |");
        }

        markdown.AppendLine();
    }

    private static void WriteEventThroughputTable(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("### Event Processing Throughput (sorted by Avg - higher is better)");
        markdown.AppendLine();

        // Prepare data with event throughput metrics
        var eventsData = reports.Select(r =>
        {
            var eventsSummary = StatisticsCalculator.CalculateThroughputSummary(
                r.EventsThroughputSamples,
                s => s.Rate,
                s => s.CumulativeCount);

            return new
            {
                Report = r,
                Avg = eventsSummary.AvgRate,
                Peak = eventsSummary.PeakRate,
                Min = eventsSummary.MinRate,
                StdDev = eventsSummary.StdDevRate,
                Cv = eventsSummary.CvRate
            };
        }).OrderByDescending(d => d.Avg).ToList();

        // Get top 3 for each column
        var (firstAvg, secondAvg, thirdAvg) = GetTop3Values(eventsData, d => d.Avg, SortDirection.HigherIsBetter);
        var (firstPeak, secondPeak, thirdPeak) = GetTop3Values(eventsData, d => d.Peak, SortDirection.HigherIsBetter);
        var (firstMin, secondMin, thirdMin) = GetTop3Values(eventsData, d => d.Min, SortDirection.HigherIsBetter);
        var (firstStdDev, secondStdDev, thirdStdDev) = GetTop3Values(eventsData, d => d.StdDev, SortDirection.LowerIsBetter);
        var (firstCv, secondCv, thirdCv) = GetTop3Values(eventsData, d => d.Cv, SortDirection.LowerIsBetter);

        // Table header
        markdown.AppendLine("| # | Process | Avg (events/s) | Peak (events/s) | Min (events/s)* | Std Dev** | CV%*** |");
        markdown.AppendLine("|---|---------|----------------|-----------------|-----------------|-----------|--------|");

        // Table rows
        for (int i = 0; i < eventsData.Count; i++)
        {
            var data = eventsData[i];

            var avgStr = AddMedal(FormatNumber(data.Avg, 2), data.Avg, firstAvg, secondAvg, thirdAvg);
            var peakStr = AddMedal(FormatNumber(data.Peak, 2), data.Peak, firstPeak, secondPeak, thirdPeak);
            var minStr = AddMedal(FormatNumber(data.Min, 2), data.Min, firstMin, secondMin, thirdMin);
            var stdDevStr = AddMedal(FormatNumber(data.StdDev, 2), data.StdDev, firstStdDev, secondStdDev, thirdStdDev);
            var cvStr = AddMedal(FormatNumber(data.Cv, 1), data.Cv, firstCv, secondCv, thirdCv);

            markdown.AppendLine(
                $"| {i + 1} | {data.Report.MonitoredProcess.Name} | {avgStr} | " +
                $"{peakStr} | {minStr} | {stdDevStr} | {cvStr} |");
        }

        markdown.AppendLine();
        markdown.AppendLine("*Min (events/s): Lowest throughput recorded during testing (excludes zero values). Higher values indicate better worst-case performance.");
        markdown.AppendLine();
        markdown.AppendLine("**Std Dev: Standard deviation measures throughput variability. Lower values indicate more consistent performance.");
        markdown.AppendLine();
        markdown.AppendLine("***CV%: Coefficient of variation (std dev / mean × 100). Lower values indicate more stable relative performance.");
        markdown.AppendLine();
    }

    private static void WriteApiThroughputTable(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("### API Throughput (sorted by Avg - higher is better)");
        markdown.AppendLine();

        // Prepare data with API throughput metrics
        var apiData = reports.Select(r =>
        {
            var apiSummary = StatisticsCalculator.CalculateThroughputSummary(
                r.ApiThroughputSamples,
                s => s.Rate,
                s => s.CumulativeCount);

            return new
            {
                Report = r,
                TotalRequests = r.Results.Phase3Api.TotalRequests,
                Avg = apiSummary.AvgRate,
                Peak = apiSummary.PeakRate,
                Min = apiSummary.MinRate,
                StdDev = apiSummary.StdDevRate,
                Cv = apiSummary.CvRate,
                AvgResponseTime = apiSummary.AvgResponseTimeMs
            };
        }).OrderByDescending(d => d.Avg).ToList();

        // Get top 3 for each column
        var (firstTotal, secondTotal, thirdTotal) = GetTop3Values(apiData, d => d.TotalRequests, SortDirection.HigherIsBetter);
        var (firstAvg, secondAvg, thirdAvg) = GetTop3Values(apiData, d => d.Avg, SortDirection.HigherIsBetter);
        var (firstPeak, secondPeak, thirdPeak) = GetTop3Values(apiData, d => d.Peak, SortDirection.HigherIsBetter);
        var (firstMin, secondMin, thirdMin) = GetTop3Values(apiData, d => d.Min, SortDirection.HigherIsBetter);
        var (firstStdDev, secondStdDev, thirdStdDev) = GetTop3Values(apiData, d => d.StdDev, SortDirection.LowerIsBetter);
        var (firstCv, secondCv, thirdCv) = GetTop3Values(apiData, d => d.Cv, SortDirection.LowerIsBetter);
        var (firstResponse, secondResponse, thirdResponse) = GetTop3Values(apiData, d => d.AvgResponseTime, SortDirection.LowerIsBetter);

        // Table header
        markdown.AppendLine("| # | Process | Total Requests | Avg (calls/s) | Peak (calls/s) | Min (calls/s)* | Std Dev** | CV%*** | Avg Response Time (ms) |");
        markdown.AppendLine("|---|---------|----------------|---------------|----------------|----------------|-----------|--------|------------------------|");

        // Table rows
        for (int i = 0; i < apiData.Count; i++)
        {
            var data = apiData[i];

            var totalStr = AddMedal(data.TotalRequests.ToString(), data.TotalRequests, firstTotal, secondTotal, thirdTotal);
            var avgStr = AddMedal(FormatNumber(data.Avg, 2), data.Avg, firstAvg, secondAvg, thirdAvg);
            var peakStr = AddMedal(FormatNumber(data.Peak, 2), data.Peak, firstPeak, secondPeak, thirdPeak);
            var minStr = AddMedal(FormatNumber(data.Min, 2), data.Min, firstMin, secondMin, thirdMin);
            var stdDevStr = AddMedal(FormatNumber(data.StdDev, 2), data.StdDev, firstStdDev, secondStdDev, thirdStdDev);
            var cvStr = AddMedal(FormatNumber(data.Cv, 1), data.Cv, firstCv, secondCv, thirdCv);
            var responseStr = AddMedal(FormatNumber(data.AvgResponseTime, 3), data.AvgResponseTime, firstResponse, secondResponse, thirdResponse);

            markdown.AppendLine(
                $"| {i + 1} | {data.Report.MonitoredProcess.Name} | {totalStr} | " +
                $"{avgStr} | {peakStr} | {minStr} | {stdDevStr} | {cvStr} | {responseStr} |");
        }

        markdown.AppendLine();
        markdown.AppendLine("*Min (calls/s): Lowest throughput recorded during testing (excludes zero values). Higher values indicate better worst-case performance.");
        markdown.AppendLine();
        markdown.AppendLine("**Std Dev: Standard deviation measures throughput variability. Lower values indicate more consistent performance.");
        markdown.AppendLine();
        markdown.AppendLine("***CV%: Coefficient of variation (std dev / mean × 100). Lower values indicate more stable relative performance.");
        markdown.AppendLine();
    }

    private static void WriteCpuUsageTable(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("### Process CPU Usage (sorted by Avg CPU % - lower is better)");
        markdown.AppendLine();

        // Prepare data
        var cpuData = reports.Select(r =>
        {
            var cpuValues = r.ProcessResourceSamples.Select(s => s.CpuPercent).ToList();
            var avgCpu = StatisticsCalculator.CalculateAverage(cpuValues);
            var peakCpu = cpuValues.Any() ? cpuValues.Max() : 0.0;

            return new
            {
                Report = r,
                AvgCpu = avgCpu,
                PeakCpu = peakCpu
            };
        }).OrderBy(d => d.AvgCpu).ToList();

        // Get top 3 for each column (lower is better)
        var (firstAvg, secondAvg, thirdAvg) = GetTop3Values(cpuData, d => d.AvgCpu, SortDirection.LowerIsBetter);
        var (firstPeak, secondPeak, thirdPeak) = GetTop3Values(cpuData, d => d.PeakCpu, SortDirection.LowerIsBetter);

        // Table header
        markdown.AppendLine("| # | Process | Avg CPU % | Peak CPU % |");
        markdown.AppendLine("|---|---------|-----------|------------|");

        // Table rows
        for (int i = 0; i < cpuData.Count; i++)
        {
            var data = cpuData[i];

            var avgStr = AddMedal(FormatNumber(data.AvgCpu, 2), data.AvgCpu, firstAvg, secondAvg, thirdAvg);
            var peakStr = AddMedal(FormatNumber(data.PeakCpu, 2), data.PeakCpu, firstPeak, secondPeak, thirdPeak);

            markdown.AppendLine($"| {i + 1} | {data.Report.MonitoredProcess.Name} | {avgStr} | {peakStr} |");
        }

        markdown.AppendLine();
    }

    private static void WriteMemoryUsageTable(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("### Process Memory Usage (sorted by Avg - lower is better)");
        markdown.AppendLine();

        // Prepare data
        var memoryData = reports.Select(r =>
        {
            var memoryValues = r.ProcessResourceSamples.Select(s => s.MemoryRssMb).ToList();
            var avgMemory = StatisticsCalculator.CalculateAverage(memoryValues);
            var peakMemory = memoryValues.Any() ? memoryValues.Max() : 0.0;

            return new
            {
                Report = r,
                AvgMemory = avgMemory,
                PeakMemory = peakMemory
            };
        }).OrderBy(d => d.AvgMemory).ToList();

        // Get top 3 for each column (lower is better)
        var (firstAvg, secondAvg, thirdAvg) = GetTop3Values(memoryData, d => d.AvgMemory, SortDirection.LowerIsBetter);
        var (firstPeak, secondPeak, thirdPeak) = GetTop3Values(memoryData, d => d.PeakMemory, SortDirection.LowerIsBetter);

        // Table header
        markdown.AppendLine("| # | Process | Avg Memory (MB) | Peak Memory (MB) |");
        markdown.AppendLine("|---|---------|-----------------|------------------|");

        // Table rows
        for (int i = 0; i < memoryData.Count; i++)
        {
            var data = memoryData[i];

            var avgStr = AddMedal(FormatNumber(data.AvgMemory, 2), data.AvgMemory, firstAvg, secondAvg, thirdAvg);
            var peakStr = AddMedal(FormatNumber(data.PeakMemory, 2), data.PeakMemory, firstPeak, secondPeak, thirdPeak);

            markdown.AppendLine($"| {i + 1} | {data.Report.MonitoredProcess.Name} | {avgStr} | {peakStr} |");
        }

        markdown.AppendLine();
    }

    private static void WriteSystemMetricsTable(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("### System-Wide Metrics (sorted by Avg System CPU % - lower is better)");
        markdown.AppendLine();

        // Prepare data
        var systemData = reports.Select(r =>
        {
            var cpuValues = r.SystemResourceSamples.Select(s => s.CpuPercent).ToList();
            var memoryValues = r.SystemResourceSamples.Select(s => s.MemoryUsedMb).ToList();

            var avgCpu = StatisticsCalculator.CalculateAverage(cpuValues);
            var peakCpu = cpuValues.Any() ? cpuValues.Max() : 0.0;
            var avgMemory = StatisticsCalculator.CalculateAverage(memoryValues);

            return new
            {
                Report = r,
                AvgSysCpu = avgCpu,
                PeakSysCpu = peakCpu,
                AvgSysMemory = avgMemory
            };
        }).OrderBy(d => d.AvgSysCpu).ToList();

        // Get top 3 for each column (lower is better)
        var (firstAvgCpu, secondAvgCpu, thirdAvgCpu) = GetTop3Values(systemData, d => d.AvgSysCpu, SortDirection.LowerIsBetter);
        var (firstPeakCpu, secondPeakCpu, thirdPeakCpu) = GetTop3Values(systemData, d => d.PeakSysCpu, SortDirection.LowerIsBetter);
        var (firstAvgMem, secondAvgMem, thirdAvgMem) = GetTop3Values(systemData, d => d.AvgSysMemory, SortDirection.LowerIsBetter);

        // Table header
        markdown.AppendLine("| # | Process | Avg System CPU % | Peak System CPU % | Avg System Memory (MB) |");
        markdown.AppendLine("|---|---------|------------------|-------------------|------------------------|");

        // Table rows
        for (int i = 0; i < systemData.Count; i++)
        {
            var data = systemData[i];

            var avgCpuStr = AddMedal(FormatNumber(data.AvgSysCpu, 2), data.AvgSysCpu, firstAvgCpu, secondAvgCpu, thirdAvgCpu);
            var peakCpuStr = AddMedal(FormatNumber(data.PeakSysCpu, 2), data.PeakSysCpu, firstPeakCpu, secondPeakCpu, thirdPeakCpu);
            var avgMemStr = AddMedal(FormatNumber(data.AvgSysMemory, 2), data.AvgSysMemory, firstAvgMem, secondAvgMem, thirdAvgMem);

            markdown.AppendLine(
                $"| {i + 1} | {data.Report.MonitoredProcess.Name} | {avgCpuStr} | " +
                $"{peakCpuStr} | {avgMemStr} |");
        }

        markdown.AppendLine();
    }

    private static void WritePerformanceHighlights(StringBuilder markdown, List<TestReport> reports)
    {
        markdown.AppendLine("## Performance Highlights");
        markdown.AppendLine();

        // Collect all metrics
        var metricsData = reports.Select(r =>
        {
            var eventsSummary = StatisticsCalculator.CalculateThroughputSummary(
                r.EventsThroughputSamples,
                s => s.Rate,
                s => s.CumulativeCount);

            var apiSummary = StatisticsCalculator.CalculateThroughputSummary(
                r.ApiThroughputSamples,
                s => s.Rate,
                s => s.CumulativeCount);

            var cpuValues = r.ProcessResourceSamples.Select(s => s.CpuPercent).ToList();
            var memoryValues = r.ProcessResourceSamples.Select(s => s.MemoryRssMb).ToList();

            return new
            {
                Report = r,
                EventsThroughput = eventsSummary.AvgRate,
                EventsCv = eventsSummary.CvRate,
                ApiThroughput = apiSummary.AvgRate,
                ApiCv = apiSummary.CvRate,
                AvgCpu = StatisticsCalculator.CalculateAverage(cpuValues),
                PeakMemory = memoryValues.Any() ? memoryValues.Max() : 0.0
            };
        }).ToList();

        // Find best performers
        var fastestEvents = metricsData.MaxBy(d => d.EventsThroughput);
        var fastestApi = metricsData.MaxBy(d => d.ApiThroughput);
        var mostStableEvents = metricsData.MinBy(d => d.EventsCv);
        var mostStableApi = metricsData.MinBy(d => d.ApiCv);
        var lowestCpu = metricsData.MinBy(d => d.AvgCpu);
        var lowestMemory = metricsData.MinBy(d => d.PeakMemory);

        if (fastestEvents != null)
        {
            markdown.AppendLine($"- **Fastest Event Processing:** {fastestEvents.Report.MonitoredProcess.Name} - {FormatNumber(fastestEvents.EventsThroughput, 2)} events/s");
        }

        if (fastestApi != null)
        {
            markdown.AppendLine($"- **Fastest API Throughput:** {fastestApi.Report.MonitoredProcess.Name} - {FormatNumber(fastestApi.ApiThroughput, 2)} calls/s");
        }

        if (mostStableEvents != null)
        {
            markdown.AppendLine($"- **Most Stable Event Processing:** {mostStableEvents.Report.MonitoredProcess.Name} - CV: {FormatNumber(mostStableEvents.EventsCv, 1)}%");
        }

        if (mostStableApi != null)
        {
            markdown.AppendLine($"- **Most Stable API Throughput:** {mostStableApi.Report.MonitoredProcess.Name} - CV: {FormatNumber(mostStableApi.ApiCv, 1)}%");
        }

        if (lowestCpu != null)
        {
            markdown.AppendLine($"- **Lowest Average CPU Usage:** {lowestCpu.Report.MonitoredProcess.Name} - {FormatNumber(lowestCpu.AvgCpu, 2)}%");
        }

        if (lowestMemory != null)
        {
            markdown.AppendLine($"- **Lowest Peak Memory Usage:** {lowestMemory.Report.MonitoredProcess.Name} - {FormatNumber(lowestMemory.PeakMemory, 2)} MB");
        }

        markdown.AppendLine();
    }

    /// <summary>
    /// Gets the top 3 distinct values from a collection.
    /// Returns tuple of (first, second, third) for medal assignment.
    /// </summary>
    private static (double? First, double? Second, double? Third) GetTop3Values<T>(
        IEnumerable<T> data,
        Func<T, double> selector,
        SortDirection direction)
    {
        var sorted = direction == SortDirection.HigherIsBetter
            ? data.OrderByDescending(selector)
            : data.OrderBy(selector);

        // Get top 3 distinct values
        var distinctValues = sorted
            .Select(selector)
            .Distinct()
            .Take(3)
            .ToList();

        return (
            distinctValues.ElementAtOrDefault(0),
            distinctValues.ElementAtOrDefault(1),
            distinctValues.ElementAtOrDefault(2)
        );
    }

    /// <summary>
    /// Gets the top 3 distinct values from a collection (integer version).
    /// </summary>
    private static (int? First, int? Second, int? Third) GetTop3Values<T>(
        IEnumerable<T> data,
        Func<T, int> selector,
        SortDirection direction)
    {
        var sorted = direction == SortDirection.HigherIsBetter
            ? data.OrderByDescending(selector)
            : data.OrderBy(selector);

        var distinctValues = sorted
            .Select(selector)
            .Distinct()
            .Take(3)
            .ToList();

        return (
            distinctValues.ElementAtOrDefault(0),
            distinctValues.ElementAtOrDefault(1),
            distinctValues.ElementAtOrDefault(2)
        );
    }

    /// <summary>
    /// Adds medal emoji to value string if it matches a top 3 value.
    /// </summary>
    private static string AddMedal(string valueStr, double value, double? first, double? second, double? third)
    {
        if (Math.Abs(value - (first ?? double.MaxValue)) < 0.001)
            return $"{valueStr} 🥇";
        if (Math.Abs(value - (second ?? double.MaxValue)) < 0.001)
            return $"{valueStr} 🥈";
        if (Math.Abs(value - (third ?? double.MaxValue)) < 0.001)
            return $"{valueStr} 🥉";

        return valueStr;
    }

    /// <summary>
    /// Adds medal emoji to value string (integer version).
    /// </summary>
    private static string AddMedal(string valueStr, int value, int? first, int? second, int? third)
    {
        if (value == first)
            return $"{valueStr} 🥇";
        if (value == second)
            return $"{valueStr} 🥈";
        if (value == third)
            return $"{valueStr} 🥉";

        return valueStr;
    }

    /// <summary>
    /// Formats a number with specified decimals, handling edge cases.
    /// </summary>
    private static string FormatNumber(double value, int decimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "N/A";

        return value.ToString($"F{decimals}");
    }

    /// <summary>
    /// Formats duration in seconds to human-readable string.
    /// </summary>
    private static string FormatDuration(double seconds)
    {
        var hours = (int)(seconds / 3600);
        var minutes = (int)((seconds % 3600) / 60);
        var secs = (int)(seconds % 60);

        if (hours > 0)
            return $"{hours}h {minutes}m {secs}s";
        if (minutes > 0)
            return $"{minutes}m {secs}s";

        return $"{secs}s";
    }
}
