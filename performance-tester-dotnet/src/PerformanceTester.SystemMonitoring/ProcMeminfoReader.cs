namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Reads system memory information from /proc/meminfo.
/// </summary>
/// <remarks>
/// /proc/meminfo format:
/// MemTotal:       32768000 kB
/// MemFree:        16384000 kB
/// MemAvailable:   20000000 kB
/// ...
///
/// We use MemTotal and MemAvailable (same as Python's psutil):
/// Used = MemTotal - MemAvailable
/// Percent = (Used / MemTotal) * 100
/// </remarks>
internal sealed class ProcMeminfoReader
{
    private const string ProcMeminfoPath = "/proc/meminfo";

    /// <summary>
    /// Reads current memory information.
    /// </summary>
    /// <returns>Memory info with total, used, and percent values in MB.</returns>
    public MemoryInfo Read()
    {
        try
        {
            var lines = File.ReadAllLines(ProcMeminfoPath);
            var memInfo = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                // Format: "MemTotal:       32768000 kB"
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0) continue;

                var key = line[..colonIndex].Trim();
                var valuePart = line[(colonIndex + 1)..].Trim();

                // Extract numeric value (ignore "kB" suffix)
                var valueStr = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (valueStr != null && long.TryParse(valueStr, out var valueKb))
                {
                    memInfo[key] = valueKb;
                }
            }

            if (!memInfo.TryGetValue("MemTotal", out var memTotalKb))
            {
                throw new InvalidOperationException("MemTotal not found in /proc/meminfo");
            }

            // Use MemAvailable if present (preferred), otherwise fall back to MemFree
            long memAvailableKb;
            if (!memInfo.TryGetValue("MemAvailable", out memAvailableKb))
            {
                memAvailableKb = memInfo.GetValueOrDefault("MemFree", 0);
            }

            var usedKb = memTotalKb - memAvailableKb;
            var totalMb = memTotalKb / 1024.0;
            var usedMb = usedKb / 1024.0;
            var percent = (memTotalKb > 0) ? (usedKb * 100.0 / memTotalKb) : 0;

            return new MemoryInfo(
                TotalMb: Math.Round(totalMb, 2),
                UsedMb: Math.Round(usedMb, 2),
                Percent: Math.Round(percent, 2));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Return zeros on error (e.g., file not accessible)
            return new MemoryInfo(0, 0, 0);
        }
    }

    /// <summary>
    /// Memory information from /proc/meminfo.
    /// </summary>
    /// <param name="TotalMb">Total system memory in MB.</param>
    /// <param name="UsedMb">Used memory in MB (Total - Available).</param>
    /// <param name="Percent">Usage percentage (0-100).</param>
    public sealed record MemoryInfo(double TotalMb, double UsedMb, double Percent);
}
