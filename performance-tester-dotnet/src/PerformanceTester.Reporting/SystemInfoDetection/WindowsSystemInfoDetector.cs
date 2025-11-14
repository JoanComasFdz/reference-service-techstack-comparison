#if WINDOWS
using System.Management;
#endif

namespace PerformanceTester.Reporting.SystemInfoDetection;

/// <summary>
/// Windows-specific hardware and operating system detection.
/// Uses WMI (System.Management) for hardware queries.
/// </summary>
/// <remarks>
/// IMPORTANT: This class should ONLY contain Windows-specific code.
/// No #if directives - platform selection happens at DI registration.
///
/// Hardware Detection:
/// - CPU: Win32_Processor WMI class (model, cores, speed)
/// - RAM: Win32_PhysicalMemory WMI class (capacity, speed, type)
/// - Disks: Win32_DiskDrive WMI class (model, size, type)
///
/// All WMI queries have 5-second timeouts to prevent hanging.
/// Results are cached using Lazy&lt;T&gt; for performance.
/// </remarks>
internal sealed class WindowsSystemInfoDetector : ISystemInfoDetector
{
    private readonly Lazy<Task<SystemInfo?>> _systemInfoCache;

    public WindowsSystemInfoDetector()
    {
        // Lazy initialization with async factory
        _systemInfoCache = new Lazy<Task<SystemInfo?>>(() => DetectSystemInfoInternalAsync(CancellationToken.None));
    }

    /// <inheritdoc />
    public Task<SystemInfo?> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        return _systemInfoCache.Value;
    }

    /// <summary>
    /// Internal detection logic (called once, result cached).
    /// </summary>
    private async Task<SystemInfo?> DetectSystemInfoInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var os = "Windows";
            var osRelease = Environment.OSVersion.Version.ToString();
            var osVersion = Environment.OSVersion.VersionString;

            var cpuInfo = await GetCpuInfoAsync(cancellationToken);
            var ramInfo = await GetRamInfoAsync(cancellationToken);
            var disks = await GetDiskInfoAsync(cancellationToken);

            return new SystemInfo
            {
                Os = os,
                OsRelease = osRelease,
                OsVersion = osVersion,
                WslVersion = null, // Not applicable for native Windows
                Cpu = cpuInfo,
                Ram = ramInfo,
                Disks = disks
            };
        }
        catch (Exception)
        {
            // Detection failure - return null
            return null;
        }
    }

    /// <summary>
    /// Gets CPU info using WMI Win32_Processor class.
    /// </summary>
    private async Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // WMI queries are synchronous, run on thread pool
            return await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                using var results = searcher.Get();

                var processor = results.Cast<ManagementObject>().FirstOrDefault();
                if (processor == null)
                {
                    return CreateFallbackCpuInfo();
                }

                var model = processor["Name"]?.ToString() ?? "Unknown CPU";
                var logicalProcessors = Environment.ProcessorCount;
                var physicalProcessors = int.Parse(processor["NumberOfCores"]?.ToString() ?? "1");
                var speedMhz = double.Parse(processor["MaxClockSpeed"]?.ToString() ?? "0");

                return new CpuInfo
                {
                    Model = model.Trim(),
                    LogicalProcessors = logicalProcessors,
                    PhysicalProcessors = physicalProcessors,
                    SpeedMhz = speedMhz > 0 ? speedMhz : null
                };
            }, cts.Token);
        }
        catch
        {
            return CreateFallbackCpuInfo();
        }
#else
        // Non-Windows: return fallback
        return await Task.FromResult(CreateFallbackCpuInfo());
#endif
    }

    /// <summary>
    /// Gets RAM info using WMI Win32_PhysicalMemory class.
    /// </summary>
    private async Task<RamInfo> GetRamInfoAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            return await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                using var results = searcher.Get();

                var memories = results.Cast<ManagementObject>().ToList();
                if (memories.Count == 0)
                {
                    return CreateFallbackRamInfo();
                }

                // Total capacity (bytes to GB)
                var totalBytes = memories.Sum(m => Convert.ToInt64(m["Capacity"]));
                var totalGb = totalBytes / 1024.0 / 1024.0 / 1024.0;

                // Speed (MHz) - use first module
                var speedMhz = memories.First()["Speed"]?.ToString();

                // Memory type (SMBIOSMemoryType)
                var memoryType = memories.First()["SMBIOSMemoryType"]?.ToString();
                var typeString = MapMemoryType(memoryType);

                // Manufacturer
                var manufacturer = memories.First()["Manufacturer"]?.ToString()?.Trim();

                return new RamInfo
                {
                    TotalGb = Math.Round(totalGb, 2),
                    Speed = speedMhz != null ? $"{speedMhz} MHz" : null,
                    Type = typeString,
                    Manufacturer = manufacturer
                };
            }, cts.Token);
        }
        catch
        {
            return CreateFallbackRamInfo();
        }
#else
        return await Task.FromResult(CreateFallbackRamInfo());
#endif
    }

    /// <summary>
    /// Gets disk info using WMI Win32_DiskDrive class.
    /// </summary>
    private async Task<IReadOnlyList<DiskInfo>> GetDiskInfoAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            return await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                using var results = searcher.Get();

                var disks = new List<DiskInfo>();

                foreach (var disk in results.Cast<ManagementObject>())
                {
                    var model = disk["Model"]?.ToString()?.Trim();
                    var sizeBytes = Convert.ToInt64(disk["Size"] ?? 0);
                    var sizeGb = sizeBytes / 1024.0 / 1024.0 / 1024.0;
                    var mediaType = disk["MediaType"]?.ToString()?.Trim();

                    // Filter: Only disks >= 500GB (matches Python behavior)
                    if (sizeGb < 500.0)
                        continue;

                    // Determine disk type (SSD vs HDD)
                    var type = DetermineWindowsDiskType(mediaType);

                    // Format size as human-readable string (matches Python format)
                    var sizeFormatted = FormatDiskSize(sizeGb);

                    disks.Add(new DiskInfo
                    {
                        Name = model ?? "Unknown Disk",
                        Size = sizeFormatted,
                        Type = type,
                        Model = model
                    });
                }

                return (IReadOnlyList<DiskInfo>)disks.AsReadOnly();
            }, cts.Token);
        }
        catch
        {
            return Array.Empty<DiskInfo>();
        }
#else
        return await Task.FromResult<IReadOnlyList<DiskInfo>>(Array.Empty<DiskInfo>());
#endif
    }

    /// <summary>
    /// Maps SMBIOS memory type code to string.
    /// Matches Python implementation (system_info.py line 86-91).
    /// </summary>
    private static string? MapMemoryType(string? memoryTypeCode)
    {
        if (memoryTypeCode == null || !int.TryParse(memoryTypeCode, out var code))
            return null;

        return code switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            _ => null
        };
    }

    /// <summary>
    /// Determines disk type from WMI MediaType property.
    /// </summary>
    private static string DetermineWindowsDiskType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
            return "Unknown";

        var lower = mediaType.ToLowerInvariant();

        if (lower.Contains("ssd") || lower.Contains("solid state"))
            return "SSD";

        if (lower.Contains("fixed") || lower.Contains("hard disk"))
            return "HDD";

        return "Unknown";
    }

    /// <summary>
    /// Formats disk size in human-readable format (matches Python output).
    /// Examples: "2.0T", "500.0G"
    /// </summary>
    private static string FormatDiskSize(double sizeGb)
    {
        if (sizeGb >= 1000.0)
        {
            // Terabytes
            var sizeTb = sizeGb / 1000.0;
            return $"{sizeTb:F1}T";
        }
        else
        {
            // Gigabytes
            return $"{sizeGb:F1}G";
        }
    }

    private static CpuInfo CreateFallbackCpuInfo() => new()
    {
        Model = "Unknown CPU",
        LogicalProcessors = Environment.ProcessorCount,
        PhysicalProcessors = 1,
        SpeedMhz = null
    };

    private static RamInfo CreateFallbackRamInfo() => new()
    {
        TotalGb = 0.0,
        Speed = null,
        Type = null,
        Manufacturer = null
    };
}
