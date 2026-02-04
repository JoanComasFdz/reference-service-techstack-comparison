# Disk Type Detection Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the disk type detection to correctly identify NVMe SSDs in WSL2 environments, matching Python tester behavior.

**Architecture:** The .NET tester uses `Win32_DiskDrive` WMI class for disk detection in WSL2, but this returns generic MediaType values. The Python tester uses `Get-PhysicalDisk` which correctly identifies SSD/HDD/NVMe. We need to switch the .NET implementation to use `Get-PhysicalDisk` PowerShell cmdlet.

**Tech Stack:** C# 13, PowerShell via Process.Start, System.Text.Json for parsing

---

## Background

### Current Behavior
- .NET tester shows: `Type: HDD` for Samsung NVMe SSDs
- Uses `Win32_DiskDrive` WMI class with `MediaType` property
- `MediaType` returns values like "Fixed hard disk media" which doesn't distinguish SSD

### Expected Behavior
- Should show: `Type: SSD (NVMe)` for NVMe SSDs
- Python uses `Get-PhysicalDisk` cmdlet which returns proper `MediaType` (SSD/HDD) and `BusType` (NVMe/SATA)

### Root Cause
The `LinuxSystemInfoDetector.GetDiskInfoWsl2Async()` method (lines 364-422) uses:
```csharp
Arguments = "-Command \"Get-CimInstance Win32_DiskDrive | Select-Object Model, Size, MediaType | ConvertTo-Json\""
```

The Python equivalent in `system_info.py` (lines 177-191) uses:
```python
ps_script = """
Get-PhysicalDisk | ForEach-Object {
    $mediaType = switch ($disk.MediaType) {
        'SSD' { 'SSD' }
        'HDD' { 'HDD' }
        ...
    }
    $busType = $disk.BusType
    ...
}
"""
```

---

## Task 1: Create WslDiskInfo Model with BusType

**Files:**
- Modify: `src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs:435-443`

**Step 1: Write the failing test**

Create test file `src/PerformanceTester.Reporting.IntegrationTests/SystemInfoDetection/DiskTypeDetectionTests.cs`:

```csharp
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.SystemInfoDetection;

namespace PerformanceTester.Reporting.IntegrationTests.SystemInfoDetection;

public class DiskTypeDetectionTests
{
    [Fact(Skip = "Integration test - requires WSL2 environment")]
    public async Task GetSystemInfoAsync_OnWsl2_ShouldDetectNvmeSsdCorrectly()
    {
        // Arrange
        var detector = new LinuxSystemInfoDetector();

        // Act
        var systemInfo = await detector.GetSystemInfoAsync();

        // Assert
        Assert.NotNull(systemInfo);
        Assert.NotEmpty(systemInfo.Disks);

        // At least one disk should be detected as SSD (not HDD) if it's an NVMe drive
        var nvmeDisk = systemInfo.Disks.FirstOrDefault(d =>
            d.Model?.Contains("SAMSUNG") == true ||
            d.Name?.Contains("NVMe") == true);

        if (nvmeDisk != null)
        {
            Assert.Contains("SSD", nvmeDisk.Type);
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test src/PerformanceTester.Reporting.IntegrationTests --filter "DiskTypeDetectionTests" -v n`
Expected: FAIL (test skipped, but we'll unskip after implementation)

**Step 3: Update WslDiskInfo model to include BusType**

In `LinuxSystemInfoDetector.cs`, find the `WslDiskInfo` class (around line 435) and update:

```csharp
/// <summary>
/// Helper class for deserializing PowerShell JSON output from Get-PhysicalDisk.
/// </summary>
private class WslDiskInfo
{
    public string? FriendlyName { get; set; }  // Changed from Model
    public long Size { get; set; }
    public string? MediaType { get; set; }
    public string? BusType { get; set; }  // Added for NVMe detection
}
```

**Step 4: Run build to verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting`
Expected: PASS (no errors)

**Step 5: Commit**

```bash
git add src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs
git add src/PerformanceTester.Reporting.IntegrationTests/SystemInfoDetection/DiskTypeDetectionTests.cs
git commit -m "feat(reporting): add BusType to WslDiskInfo model for NVMe detection

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Update PowerShell Command to Use Get-PhysicalDisk

**Files:**
- Modify: `src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs:364-422`

**Step 1: Update the PowerShell command**

Find `GetDiskInfoWsl2Async` method and replace the PowerShell command:

OLD (around line 374):
```csharp
Arguments = "-Command \"Get-CimInstance Win32_DiskDrive | Select-Object Model, Size, MediaType | ConvertTo-Json\"",
```

NEW:
```csharp
Arguments = "-Command \"Get-PhysicalDisk | Select-Object FriendlyName, Size, MediaType, BusType | ConvertTo-Json\"",
```

**Step 2: Update the disk parsing logic**

Find the loop that processes `wslDisks` (around line 398-415) and update:

OLD:
```csharp
foreach (var d in wslDisks)
{
    var sizeGb = d.Size / 1024.0 / 1024.0 / 1024.0;

    // Filter: Only disks >= 500GB (matches Python behavior)
    if (sizeGb < 500.0)
        continue;

    disks.Add(new DiskInfo
    {
        Name = d.Model ?? "Unknown Disk",
        Size = FormatDiskSize(sizeGb),
        Type = DetermineWindowsDiskType(d.MediaType),
        Model = d.Model
    });
}
```

NEW:
```csharp
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
```

**Step 3: Run build to verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting`
Expected: FAIL (DeterminePhysicalDiskType doesn't exist yet)

**Step 4: Commit partial progress**

```bash
git add src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs
git commit -m "refactor(reporting): switch WSL2 disk detection to Get-PhysicalDisk

Uses Get-PhysicalDisk instead of Win32_DiskDrive for more accurate
disk type detection including NVMe identification.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Implement DeterminePhysicalDiskType Method

**Files:**
- Modify: `src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs`

**Step 1: Add new method after DetermineWindowsDiskType**

Find `DetermineWindowsDiskType` method (around line 484) and add new method after it:

```csharp
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
```

**Step 2: Run build to verify compilation**

Run: `dotnet build src/PerformanceTester.Reporting`
Expected: PASS

**Step 3: Run existing tests to verify no regression**

Run: `dotnet test src/PerformanceTester.Reporting.IntegrationTests -v n`
Expected: PASS (all existing tests should still pass)

**Step 4: Commit**

```bash
git add src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs
git commit -m "feat(reporting): add DeterminePhysicalDiskType for accurate SSD/NVMe detection

Maps Get-PhysicalDisk MediaType (SSD/HDD) and BusType (NVMe/SATA)
to human-readable format matching Python tester output.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Update WindowsSystemInfoDetector (for consistency)

**Files:**
- Modify: `src/PerformanceTester.Reporting/SystemInfoDetection/WindowsSystemInfoDetector.cs`

**Step 1: Check current implementation**

Read and understand the Windows detector's disk detection logic.

**Step 2: Update to use same Get-PhysicalDisk approach**

If the Windows detector uses `Win32_DiskDrive`, update it to use `Get-PhysicalDisk` for consistency.
The changes should mirror what was done for LinuxSystemInfoDetector.

**Step 3: Run build and tests**

Run: `dotnet build && dotnet test src/PerformanceTester.Reporting.IntegrationTests -v n`
Expected: PASS

**Step 4: Commit**

```bash
git add src/PerformanceTester.Reporting/SystemInfoDetection/WindowsSystemInfoDetector.cs
git commit -m "refactor(reporting): update Windows disk detection to use Get-PhysicalDisk

Consistent disk type detection across Windows and WSL2 environments.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 5: Manual Integration Test

**Files:**
- None (manual verification)

**Step 1: Run full test suite with dotnet tester**

```bash
cd /workspace
./scripts/tools/testing/run-all-tests.sh --events 100 --duration 5s -t dotnet
```

**Step 2: Verify disk type in generated report**

```bash
grep -A20 "Storage" /workspace/performance-tester-dotnet/test-results/test-report-comparison-*.md | tail -20
```

Expected output should show `SSD (NVMe)` instead of `HDD`:
```
**Storage (2 physical drive(s) on Windows host):**

**Drive 1:** SAMSUNG MZVL21T0HDLU-00B07
- **Type:** SSD (NVMe)
- **Model:** SAMSUNG MZVL21T0HDLU-00B07
- **Capacity:** 953.9G
```

**Step 3: Compare with Python tester output**

The disk types should now match or be more accurate than the Python tester.

---

## Summary of Changes

| File | Change |
|------|--------|
| `LinuxSystemInfoDetector.cs` | Switch from `Win32_DiskDrive` to `Get-PhysicalDisk`, add `DeterminePhysicalDiskType` method |
| `WindowsSystemInfoDetector.cs` | Same changes for consistency |
| `DiskTypeDetectionTests.cs` | New integration test for disk type detection |

## Expected Outcomes

1. NVMe SSDs correctly identified as `SSD (NVMe)` instead of `HDD`
2. SATA SSDs identified as `SSD (SATA)` instead of just `SSD`
3. HDDs still correctly identified as `HDD`
4. Virtual disks properly filtered out
5. All existing tests continue to pass
