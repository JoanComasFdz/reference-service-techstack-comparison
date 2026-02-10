using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.Reporting.ChartGeneration.DataLoading;

/// <summary>
/// Loads chart data from JSON files.
/// </summary>
internal static class ChartDataLoader
{
    /// <summary>
    /// Loads throughput report from JSON file.
    /// Handles both events and API throughput formats.
    /// </summary>
    public static ThroughputReport? LoadThroughputReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var samples = ParseThroughputSamples(root);
            var summary = ParseThroughputSummary(root);

            return new ThroughputReport
            {
                TestDate = DateTime.Parse(root.GetProperty("test_date").GetString()!),
                SamplingIntervalMs = root.GetProperty("sampling_interval_ms").GetInt32(),
                Samples = samples,
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load throughput report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads resource metrics report from JSON file.
    /// Used for RabbitMQ, PostgreSQL, and system metrics.
    /// </summary>
    public static ResourceMetricsReport? LoadResourceReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var samples = ParseResourceSamples(root);
            var (cpuSummary, memorySummary) = ParseResourceSummary(root, samples);

            return new ResourceMetricsReport
            {
                TestDate = root.TryGetProperty("test_date", out var td) ? DateTime.Parse(td.GetString()!) : DateTime.MinValue,
                SamplingIntervalMs = root.TryGetProperty("sampling_interval_ms", out var si) ? si.GetInt32() : 500,
                Samples = samples,
                CpuSummary = cpuSummary,
                MemorySummary = memorySummary
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load resource report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads process resource metrics report from JSON file.
    /// Used for service (monitored process) metrics.
    /// </summary>
    public static ResourceMetricsReport? LoadProcessResourceReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var samples = ParseProcessResourceSamples(root);
            var (cpuSummary, memorySummary) = ParseProcessResourceSummary(root, samples);

            return new ResourceMetricsReport
            {
                TestDate = root.TryGetProperty("test_date", out var td) ? DateTime.Parse(td.GetString()!) : DateTime.MinValue,
                SamplingIntervalMs = root.TryGetProperty("sampling_interval_ms", out var si) ? si.GetInt32() : 500,
                Samples = samples,
                CpuSummary = cpuSummary,
                MemorySummary = memorySummary
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load process resource report from {FilePath}", filePath);
            return null;
        }
    }

    #region Throughput Parsing

    private static List<ThroughputSampleJson> ParseThroughputSamples(JsonElement root)
    {
        var samples = new List<ThroughputSampleJson>();
        if (!root.TryGetProperty("samples", out var samplesArray))
            return samples;

        foreach (var sample in samplesArray.EnumerateArray())
        {
            var timestamp = DateTime.Parse(sample.GetProperty("timestamp").GetString()!);
            var elapsedSeconds = sample.GetProperty("elapsed_seconds").GetDouble();

            // Get rate from either events_per_second or calls_per_second
            double rate = 0;
            if (sample.TryGetProperty("events_per_second", out var eventsRate))
                rate = eventsRate.GetDouble();
            else if (sample.TryGetProperty("calls_per_second", out var callsRate))
                rate = callsRate.GetDouble();

            // Get count from either total_events or total_calls
            int count = 0;
            if (sample.TryGetProperty("total_events", out var totalEvents))
                count = totalEvents.GetInt32();
            else if (sample.TryGetProperty("total_calls", out var totalCalls))
                count = totalCalls.GetInt32();

            samples.Add(new ThroughputSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = elapsedSeconds,
                EventsPerSecond = rate,
                TotalEvents = count
            });
        }

        return samples;
    }

    private static ThroughputSummary ParseThroughputSummary(JsonElement root)
    {
        var summary = root.GetProperty("summary");

        return new ThroughputSummary
        {
            AvgRate = GetDoubleFromEither(summary, "avg_events_per_second", "avg_calls_per_second"),
            PeakRate = GetDoubleFromEither(summary, "peak_events_per_second", "peak_calls_per_second"),
            MinRate = GetDoubleFromEither(summary, "min_events_per_second", "min_calls_per_second"),
            StdDevRate = GetDoubleFromEither(summary, "std_dev_events_per_second", "std_dev_calls_per_second"),
            CvRate = GetDoubleFromEither(summary, "cv_events_per_second", "cv_calls_per_second"),
            AvgResponseTimeMs = summary.TryGetProperty("avg_response_time_ms", out var rtMs) ? rtMs.GetDouble() : 0,
            TotalSamples = summary.TryGetProperty("total_samples", out var ts) ? ts.GetInt32() : 0,
            TotalCount = GetIntFromEither(summary, "total_events", "total_calls")
        };
    }

    #endregion

    #region Resource Parsing

    private static List<ResourceSampleJson> ParseResourceSamples(JsonElement root)
    {
        var samples = new List<ResourceSampleJson>();
        if (!root.TryGetProperty("samples", out var samplesArray))
            return samples;

        foreach (var sample in samplesArray.EnumerateArray())
        {
            var timestamp = DateTime.Parse(sample.GetProperty("timestamp").GetString()!);
            var cpuPercent = sample.TryGetProperty("cpu_percent", out var cpu) ? cpu.GetDouble() : 0;

            // Get memory from memory_mb, memory_rss_mb, or memory_used_mb
            double memoryMb = 0;
            if (sample.TryGetProperty("memory_mb", out var mem))
                memoryMb = mem.GetDouble();
            else if (sample.TryGetProperty("memory_rss_mb", out var rss))
                memoryMb = rss.GetDouble();
            else if (sample.TryGetProperty("memory_used_mb", out var used))
                memoryMb = used.GetDouble();

            samples.Add(new ResourceSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = 0,
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            });
        }

        return samples;
    }

    private static (ResourceSummary cpu, ResourceSummary memory) ParseResourceSummary(
        JsonElement root,
        List<ResourceSampleJson> samples)
    {
        var cpuValues = samples.Select(s => s.CpuPercent).ToList();
        var memoryValues = samples.Select(s => s.MemoryMb).ToList();

        if (root.TryGetProperty("summary", out var summary))
        {
            var cpuSummary = new ResourceSummary
            {
                Avg = summary.TryGetProperty("avg_cpu_percent", out var avgCpu) ? avgCpu.GetDouble() : 0,
                Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                Max = summary.TryGetProperty("peak_cpu_percent", out var peakCpu) ? peakCpu.GetDouble() : 0,
                Mode = CalculateMode(cpuValues),
                Unit = "%"
            };

            double avgMemory = 0;
            if (summary.TryGetProperty("avg_memory_mb", out var avgMem))
                avgMemory = avgMem.GetDouble();
            else if (summary.TryGetProperty("avg_memory_used_mb", out var avgUsed))
                avgMemory = avgUsed.GetDouble();

            double peakMemory = 0;
            if (summary.TryGetProperty("peak_memory_mb", out var peakMem))
                peakMemory = peakMem.GetDouble();
            else if (summary.TryGetProperty("peak_memory_used_mb", out var peakUsed))
                peakMemory = peakUsed.GetDouble();

            var memorySummary = new ResourceSummary
            {
                Avg = avgMemory,
                Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                Max = peakMemory,
                Mode = CalculateMode(memoryValues),
                Unit = "MB"
            };

            return (cpuSummary, memorySummary);
        }

        // Fallback
        return (
            new ResourceSummary
            {
                Avg = 0,
                Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(cpuValues),
                Unit = "%"
            },
            new ResourceSummary
            {
                Avg = 0,
                Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(memoryValues),
                Unit = "MB"
            }
        );
    }

    private static List<ResourceSampleJson> ParseProcessResourceSamples(JsonElement root)
    {
        var samples = new List<ResourceSampleJson>();
        if (!root.TryGetProperty("samples", out var samplesArray))
            return samples;

        foreach (var sample in samplesArray.EnumerateArray())
        {
            var timestamp = DateTime.Parse(sample.GetProperty("timestamp").GetString()!);
            var cpuPercent = sample.TryGetProperty("cpu_percent", out var cpu) ? cpu.GetDouble() : 0;

            double memoryMb = 0;
            if (sample.TryGetProperty("memory_rss_mb", out var rss))
                memoryMb = rss.GetDouble();
            else if (sample.TryGetProperty("memory_mb", out var mem))
                memoryMb = mem.GetDouble();

            samples.Add(new ResourceSampleJson
            {
                Timestamp = timestamp,
                ElapsedSeconds = 0,
                CpuPercent = cpuPercent,
                MemoryMb = memoryMb
            });
        }

        return samples;
    }

    private static (ResourceSummary cpu, ResourceSummary memory) ParseProcessResourceSummary(
        JsonElement root,
        List<ResourceSampleJson> samples)
    {
        var cpuValues = samples.Select(s => s.CpuPercent).ToList();
        var memoryValues = samples.Select(s => s.MemoryMb).ToList();

        if (root.TryGetProperty("cpu_summary", out var cpuSum) && root.TryGetProperty("memory_summary", out var memSum))
        {
            return (
                new ResourceSummary
                {
                    Avg = cpuSum.TryGetProperty("avg", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = cpuSum.TryGetProperty("max", out var maxCpu) ? maxCpu.GetDouble() : 0,
                    Mode = CalculateMode(cpuValues),
                    Unit = cpuSum.TryGetProperty("unit", out var unitCpu) ? unitCpu.GetString() ?? "%" : "%"
                },
                new ResourceSummary
                {
                    Avg = memSum.TryGetProperty("avg", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = memSum.TryGetProperty("max", out var maxMem) ? maxMem.GetDouble() : 0,
                    Mode = CalculateMode(memoryValues),
                    Unit = memSum.TryGetProperty("unit", out var unitMem) ? unitMem.GetString() ?? "MB" : "MB"
                }
            );
        }

        if (root.TryGetProperty("summary", out var summary))
        {
            return (
                new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_cpu_percent", out var avgCpu) ? avgCpu.GetDouble() : 0,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = summary.TryGetProperty("peak_cpu_percent", out var peakCpu) ? peakCpu.GetDouble() : 0,
                    Mode = CalculateMode(cpuValues),
                    Unit = "%"
                },
                new ResourceSummary
                {
                    Avg = summary.TryGetProperty("avg_memory_rss_mb", out var avgMem) ? avgMem.GetDouble() : 0,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = summary.TryGetProperty("peak_memory_rss_mb", out var peakMem) ? peakMem.GetDouble() : 0,
                    Mode = CalculateMode(memoryValues),
                    Unit = "MB"
                }
            );
        }

        // Fallback
        return (
            new ResourceSummary
            {
                Avg = 0,
                Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(cpuValues),
                Unit = "%"
            },
            new ResourceSummary
            {
                Avg = 0,
                Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                Max = 0,
                Mode = CalculateMode(memoryValues),
                Unit = "MB"
            }
        );
    }

    #endregion

    #region Helpers

    private static double GetDoubleFromEither(JsonElement element, string key1, string key2)
    {
        if (element.TryGetProperty(key1, out var prop1))
            return prop1.GetDouble();
        if (element.TryGetProperty(key2, out var prop2))
            return prop2.GetDouble();
        return 0;
    }

    private static int GetIntFromEither(JsonElement element, string key1, string key2)
    {
        if (element.TryGetProperty(key1, out var prop1))
            return prop1.GetInt32();
        if (element.TryGetProperty(key2, out var prop2))
            return prop2.GetInt32();
        return 0;
    }

    private static int CalculateMode(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        return values
            .Select(v => (int)Math.Round(v))
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }

    #endregion
}
