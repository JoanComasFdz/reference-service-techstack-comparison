namespace PerformanceTester.Reporting;

/// <summary>
/// Hardware and operating system information for the test environment.
/// Matches Python system_info.py output format.
/// </summary>
public sealed record SystemInfo
{
    /// <summary>
    /// Operating system name (e.g., "Windows", "Linux").
    /// </summary>
    public required string Os { get; init; }

    /// <summary>
    /// Operating system release version (e.g., "11", "5.15.0").
    /// </summary>
    public required string OsRelease { get; init; }

    /// <summary>
    /// Detailed OS version string (e.g., "5.15.0-124-generic").
    /// </summary>
    public required string OsVersion { get; init; }

    /// <summary>
    /// WSL version if running under WSL, otherwise null.
    /// Possible values: "WSL1", "WSL2", null.
    /// </summary>
    public string? WslVersion { get; init; }

    /// <summary>
    /// CPU information.
    /// </summary>
    public required CpuInfo Cpu { get; init; }

    /// <summary>
    /// RAM information.
    /// </summary>
    public required RamInfo Ram { get; init; }

    /// <summary>
    /// Physical disk information (excludes virtual disks, partitions).
    /// </summary>
    public required IReadOnlyList<DiskInfo> Disks { get; init; }
}

/// <summary>
/// CPU hardware information.
/// </summary>
public sealed record CpuInfo
{
    /// <summary>
    /// CPU model name (e.g., "AMD Ryzen 9 7950X 16-Core Processor").
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Number of logical processors (threads).
    /// Equivalent to Python's os.cpu_count().
    /// </summary>
    public required int LogicalProcessors { get; init; }

    /// <summary>
    /// Number of physical processors (CPU packages/sockets).
    /// Typically 1 for consumer systems, 2+ for servers.
    /// </summary>
    public required int PhysicalProcessors { get; init; }

    /// <summary>
    /// CPU speed in MHz, or null if not available.
    /// </summary>
    public double? SpeedMhz { get; init; }
}

/// <summary>
/// RAM hardware information.
/// </summary>
public sealed record RamInfo
{
    /// <summary>
    /// Total RAM capacity in gigabytes.
    /// </summary>
    public required double TotalGb { get; init; }

    /// <summary>
    /// RAM speed (e.g., "5200 MHz"), or null if not available.
    /// </summary>
    public string? Speed { get; init; }

    /// <summary>
    /// RAM type (e.g., "DDR4", "DDR5"), or null if not available.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// RAM manufacturer (e.g., "G.Skill"), or null if not available.
    /// </summary>
    public string? Manufacturer { get; init; }
}

/// <summary>
/// Physical disk hardware information.
/// </summary>
public sealed record DiskInfo
{
    /// <summary>
    /// Friendly disk name or model (e.g., "Samsung SSD 990 PRO 2TB").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Disk size as formatted string (e.g., "2.0T", "500.0G").
    /// </summary>
    public required string Size { get; init; }

    /// <summary>
    /// Disk type (e.g., "SSD (NVMe)", "SSD", "HDD").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Disk model name, or null if not available.
    /// </summary>
    public string? Model { get; init; }
}
