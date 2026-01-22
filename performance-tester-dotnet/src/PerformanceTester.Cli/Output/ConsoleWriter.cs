using PerformanceTester.Reporting;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Default console writer with colored output.
/// </summary>
public sealed class ConsoleWriter : IConsoleWriter
{
    public void WriteHeader(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== {text} ===");
        Console.ResetColor();
    }

    public void WriteLine(string text = "")
    {
        Console.WriteLine(text);
    }

    public void WriteInfo(string text)
    {
        Console.WriteLine($"  {text}");
    }

    public void WriteSuccess(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] {text}");
        Console.ResetColor();
    }

    public void WriteWarning(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN] {text}");
        Console.ResetColor();
    }

    public void WriteError(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {text}");
        Console.ResetColor();
    }

    public void WriteConfigTable(Orchestration.TestConfiguration config)
    {
        var rows = new[]
        {
            ("Events", config.EventCount.ToString("N0")),
            ("API Duration", config.ApiDurationOrDefault.ToString()),
            ("API Workers", config.ApiWorkers.ToString()),
            ("Port", config.ServicePort.ToString()),
            ("Database", config.DatabaseName),
            ("Results Folder", config.ResultsFolder),
            ("Warmup Events", config.WarmupEventCount.ToString("N0")),
            ("Warmup API Calls", config.WarmupApiCallCount.ToString()),
            ("Inactivity Timeout", config.InactivityTimeoutOrDefault.ToString())
        };

        WriteTable(rows);
    }

    public void WriteResultsTable(TestReport report)
    {
        // Calculate resource usage stats from samples
        var peakCpu = report.ProcessResourceSamples.Count > 0
            ? report.ProcessResourceSamples.Max(s => s.CpuPercent)
            : 0.0;
        var peakMemory = report.ProcessResourceSamples.Count > 0
            ? report.ProcessResourceSamples.Max(s => s.MemoryRssMb)
            : 0.0;
        var avgThreads = report.ProcessResourceSamples.Count > 0
            ? report.ProcessResourceSamples.Average(s => s.Threads)
            : 0.0;

        var rows = new List<(string Label, string Value)>
        {
            ("Service", report.MonitoredProcess.Name),
            ("Process ID", report.MonitoredProcess.Pid.ToString()),
            ("Total Duration", $"{report.TotalRuntimeSeconds:F1}s"),
            ("", ""),
            ("Publish Throughput", $"{report.Results.Phase1Publish.ThroughputEventsPerSec:F1} events/sec"),
            ("Consume Throughput", $"{report.Results.Phase2Consume.ThroughputEventsPerSec:F1} events/sec"),
            ("", ""),
            ("API Throughput", $"{report.Results.Phase3Api.ThroughputCallsPerSec:F1} req/sec"),
            ("Total Requests", report.Results.Phase3Api.TotalRequests.ToString("N0")),
            ("Success Rate", $"{report.Results.Phase3Api.SuccessPercentage:F1}%"),
            ("", ""),
            ("Peak CPU", $"{peakCpu:F1}%"),
            ("Peak Memory", $"{peakMemory:F1} MB"),
            ("Avg Threads", avgThreads.ToString("F0"))
        };

        WriteTable(rows);
    }

    private void WriteTable(IEnumerable<(string Label, string Value)> rows)
    {
        foreach (var (label, value) in rows)
        {
            if (string.IsNullOrEmpty(label))
            {
                Console.WriteLine();
                continue;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  {label,-20}");
            Console.ResetColor();
            Console.WriteLine(value);
        }
    }
}
