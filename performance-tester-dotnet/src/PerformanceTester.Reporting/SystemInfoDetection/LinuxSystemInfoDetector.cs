namespace PerformanceTester.Reporting.SystemInfoDetection;

/// <summary>
/// Linux-specific hardware and operating system detection.
/// Parses /proc files for hardware information.
/// </summary>
/// <remarks>
/// IMPORTANT: This class should ONLY contain Linux-specific code.
/// No #if directives - platform selection happens at DI registration.
///
/// Hardware Detection:
/// - CPU: Parses /proc/cpuinfo (model name, physical id, cpu MHz)
/// - RAM: Parses /proc/meminfo (MemTotal)
/// - Disks: Executes lsblk command (name, size, type)
/// - WSL: Detects WSL2 via /proc/version and /run/WSL
///
/// For WSL2, queries Windows host via PowerShell for accurate RAM/disk info.
/// All operations have timeouts to prevent hanging.
/// Results are cached using Lazy&lt;T&gt; for performance.
/// </remarks>
internal sealed class LinuxSystemInfoDetector : ISystemInfoDetector
{
    private readonly Lazy<Task<SystemInfo?>> _systemInfoCache;

    public LinuxSystemInfoDetector()
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
            var os = "Linux";
            var distroName = await GetLinuxDistroNameAsync();
            var osRelease = distroName ?? Environment.OSVersion.Version.ToString(); // Fallback to kernel version
            var osVersion = Environment.OSVersion.VersionString;
            var wslVersion = await DetectWslVersionAsync();

            var cpuInfo = await GetCpuInfoAsync(cancellationToken);
            var ramInfo = await GetRamInfoAsync(wslVersion, cancellationToken);
            var disks = await GetDiskInfoAsync(wslVersion, cancellationToken);

            return new SystemInfo
            {
                Os = os,
                OsRelease = osRelease,
                OsVersion = osVersion,
                WslVersion = wslVersion,
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
    /// Detects WSL version by reading /proc/version.
    /// Returns "WSL1", "WSL2", or null.
    /// </summary>
    private static async Task<string?> DetectWslVersionAsync()
    {
        const string procVersionPath = "/proc/version";
        if (!File.Exists(procVersionPath))
            return null;

        try
        {
            var versionInfo = (await File.ReadAllTextAsync(procVersionPath)).ToLowerInvariant();

            if (!versionInfo.Contains("microsoft") && !versionInfo.Contains("wsl"))
                return null;

            if (versionInfo.Contains("wsl2") || Directory.Exists("/run/WSL"))
                return "WSL2";

            return "WSL1";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets Linux distribution name by parsing /etc/os-release.
    /// Returns PRETTY_NAME (e.g., "Ubuntu 24.04.3 LTS"), or null if unavailable.
    /// </summary>
    private static async Task<string?> GetLinuxDistroNameAsync()
    {
        const string osReleasePath = "/etc/os-release";
        if (!File.Exists(osReleasePath))
            return null;

        try
        {
            var lines = await File.ReadAllLinesAsync(osReleasePath);

            // Parse key=value pairs into dictionary
            var parsed = lines
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1].Trim('"'));

            var prettyName = parsed.GetValueOrDefault("PRETTY_NAME");
            var name = parsed.GetValueOrDefault("NAME");
            var versionId = parsed.GetValueOrDefault("VERSION_ID");

            // Prefer PRETTY_NAME (e.g., "Ubuntu 24.04.3 LTS"), fall back to NAME + VERSION_ID
            if (!string.IsNullOrEmpty(prettyName))
                return prettyName;

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(versionId))
                return $"{name} {versionId}";

            return name; // May be null
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets CPU info by parsing /proc/cpuinfo.
    /// </summary>
    private async Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken)
    {
        const string cpuInfoPath = "/proc/cpuinfo";
        if (!File.Exists(cpuInfoPath))
        {
            return CreateFallbackCpuInfo();
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(cpuInfoPath, cancellationToken);

            string? modelName = null;
            double? speedMhz = null;
            var physicalIds = new HashSet<string>();

            foreach (var line in lines)
            {
                if (line.Contains("model name") && modelName == null)
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2)
                        modelName = parts[1].Trim();
                }
                else if (line.Contains("cpu MHz") && speedMhz == null)
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out var mhz))
                        speedMhz = mhz;
                }
                else if (line.Contains("physical id"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2)
                        physicalIds.Add(parts[1].Trim());
                }
            }

            var physicalProcessors = physicalIds.Count > 0 ? physicalIds.Count : 1;

            return new CpuInfo
            {
                Model = modelName ?? "Unknown CPU",
                LogicalProcessors = Environment.ProcessorCount,
                PhysicalProcessors = physicalProcessors,
                SpeedMhz = speedMhz
            };
        }
        catch
        {
            return CreateFallbackCpuInfo();
        }
    }

    /// <summary>
    /// Gets RAM info by parsing /proc/meminfo.
    /// For WSL2, queries Windows host via PowerShell.
    /// </summary>
    private async Task<RamInfo> GetRamInfoAsync(string? wslVersion, CancellationToken cancellationToken)
    {
        // WSL2 requires querying Windows host for accurate RAM info
        if (wslVersion == "WSL2")
        {
            var wslRam = await GetRamInfoWsl2Async(cancellationToken);
            if (wslRam != null)
                return wslRam;
        }

        // Native Linux: Parse /proc/meminfo
        const string meminfoPath = "/proc/meminfo";
        if (!File.Exists(meminfoPath))
        {
            return CreateFallbackRamInfo();
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(meminfoPath, cancellationToken);

            // Find MemTotal line
            var memTotalLine = lines.FirstOrDefault(l => l.StartsWith("MemTotal:"));
            if (memTotalLine == null)
            {
                return CreateFallbackRamInfo();
            }

            // Parse: "MemTotal:       16384000 kB"
            var parts = memTotalLine.Split(':', 2);
            if (parts.Length != 2)
            {
                return CreateFallbackRamInfo();
            }

            var valueString = parts[1].Trim().Replace(" kB", "").Trim();
            if (!long.TryParse(valueString, out var memTotalKb))
            {
                return CreateFallbackRamInfo();
            }

            var totalGb = memTotalKb / 1024.0 / 1024.0;

            return new RamInfo
            {
                TotalGb = Math.Round(totalGb, 2),
                Speed = null, // Not available from /proc/meminfo
                Type = null,
                Manufacturer = null
            };
        }
        catch
        {
            return CreateFallbackRamInfo();
        }
    }

    /// <summary>
    /// Gets RAM info on WSL2 by querying Windows host via PowerShell.
    /// Matches Python implementation (system_info.py lines 75-94).
    /// </summary>
    private async Task<RamInfo?> GetRamInfoWsl2Async(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-Command \"Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity, Speed, SMBIOSMemoryType, Manufacturer | ConvertTo-Json\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            // Parse JSON output
            var memories = JsonSerializer.Deserialize<List<WslMemoryInfo>>(output);
            if (memories == null || memories.Count == 0)
            {
                return null;
            }

            var totalBytes = memories.Sum(m => m.Capacity);
            var totalGb = totalBytes / 1024.0 / 1024.0 / 1024.0;

            var first = memories.First();
            var speedMhz = first.Speed > 0 ? $"{first.Speed} MHz" : null;
            var typeString = MapMemoryType(first.SMBIOSMemoryType.ToString());
            var manufacturer = first.Manufacturer?.Trim();

            return new RamInfo
            {
                TotalGb = Math.Round(totalGb, 2),
                Speed = speedMhz,
                Type = typeString,
                Manufacturer = manufacturer
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets disk info using lsblk command.
    /// For WSL2, queries Windows host via PowerShell.
    /// </summary>
    private async Task<IReadOnlyList<DiskInfo>> GetDiskInfoAsync(string? wslVersion, CancellationToken cancellationToken)
    {
        // WSL2 requires querying Windows host for physical disks
        if (wslVersion == "WSL2")
        {
            var wslDisks = await GetDiskInfoWsl2Async(cancellationToken);
            if (wslDisks.Count > 0)
                return wslDisks;
        }

        // Native Linux: Use lsblk command
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var psi = new ProcessStartInfo
            {
                FileName = "lsblk",
                Arguments = "-dno NAME,SIZE,TYPE --bytes",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<DiskInfo>();
            }

            var disks = new List<DiskInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                var name = parts[0];
                var sizeBytes = long.TryParse(parts[1], out var bytes) ? bytes : 0;
                var sizeGb = sizeBytes / 1024.0 / 1024.0 / 1024.0;
                var type = parts[2];

                // Only include physical disks (type=disk)
                if (type != "disk")
                    continue;

                // Filter: Only disks >= 500GB (matches Python behavior)
                if (sizeGb < 500.0)
                    continue;

                // Format size as human-readable string (matches Python format)
                var sizeFormatted = FormatDiskSize(sizeGb);

                disks.Add(new DiskInfo
                {
                    Name = name,
                    Size = sizeFormatted,
                    Type = DetermineLinuxDiskType(name),
                    Model = null
                });
            }

            return disks.AsReadOnly();
        }
        catch
        {
            return Array.Empty<DiskInfo>();
        }
    }

    /// <summary>
    /// Gets disk info on WSL2 by querying Windows host via PowerShell.
    /// </summary>
    private async Task<IReadOnlyList<DiskInfo>> GetDiskInfoWsl2Async(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-Command \"Get-PhysicalDisk | Select-Object FriendlyName, Size, MediaType, BusType | ConvertTo-Json\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<DiskInfo>();
            }

            var wslDisks = JsonSerializer.Deserialize<List<WslDiskInfo>>(output);
            if (wslDisks == null || wslDisks.Count == 0)
            {
                return Array.Empty<DiskInfo>();
            }

            var disks = new List<DiskInfo>();
            foreach (var d in wslDisks)
            {
                var sizeGb = d.Size / 1024.0 / 1024.0 / 1024.0;

                // Filter: Only disks >= 500GB (matches Python behavior)
                if (sizeGb < 500.0)
                    continue;

                // Skip virtual disks (WSL2 creates these)
                var friendlyName = d.FriendlyName ?? "";
                if (friendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    continue;

                disks.Add(new DiskInfo
                {
                    Name = friendlyName.Length > 0 ? friendlyName : "Unknown Disk",
                    Size = FormatDiskSize(sizeGb),
                    Type = DeterminePhysicalDiskType(d.MediaType, d.BusType),
                    Model = d.FriendlyName
                });
            }

            return disks.AsReadOnly();
        }
        catch
        {
            return Array.Empty<DiskInfo>();
        }
    }

    /// <summary>
    /// Helper class for deserializing PowerShell JSON output.
    /// </summary>
    private class WslMemoryInfo
    {
        public long Capacity { get; set; }
        public int Speed { get; set; }
        public int SMBIOSMemoryType { get; set; }
        public string? Manufacturer { get; set; }
    }

    /// <summary>
    /// Helper class for deserializing PowerShell JSON output from Get-PhysicalDisk.
    /// </summary>
    private class WslDiskInfo
    {
        public string? FriendlyName { get; set; }
        public long Size { get; set; }
        public string? MediaType { get; set; }
        public string? BusType { get; set; }
    }

    /// <summary>
    /// Maps SMBIOS memory type code to string.
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
    /// Determines disk type from device name (heuristic).
    /// </summary>
    private static string DetermineLinuxDiskType(string deviceName)
    {
        if (deviceName.StartsWith("nvme"))
            return "NVMe SSD";

        if (deviceName.StartsWith("sd"))
            return "SSD/HDD"; // Cannot distinguish without additional info

        if (deviceName.StartsWith("hd"))
            return "HDD";

        return "Unknown";
    }

    /// <summary>
    /// Determines disk type from Get-PhysicalDisk MediaType and BusType properties.
    /// Matches Python implementation (system_info.py lines 218-223).
    /// </summary>
    private static string DeterminePhysicalDiskType(string? mediaType, string? busType)
    {
        // Default if no info available
        if (string.IsNullOrEmpty(mediaType))
            return "Unknown";

        // Map MediaType to base type
        var baseType = mediaType.ToUpperInvariant() switch
        {
            "SSD" => "SSD",
            "HDD" => "HDD",
            "SCM" => "SCM",  // Storage Class Memory
            _ => "Unknown"
        };

        // If unknown base type, return as-is
        if (baseType == "Unknown")
            return baseType;

        // Append bus type for more specific identification
        if (!string.IsNullOrEmpty(busType))
        {
            var upperBusType = busType.ToUpperInvariant();
            if (upperBusType.Contains("NVME"))
                return $"{baseType} (NVMe)";
            if (upperBusType.Contains("SATA"))
                return $"{baseType} (SATA)";
        }

        return baseType;
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
