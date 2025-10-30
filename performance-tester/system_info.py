#!/usr/bin/env python3
"""
System information gathering utilities.

Collects CPU, RAM, disk, and OS information for performance test reports.
"""

import os
import platform
import subprocess
import json
from typing import Any, Optional


# Constants
SUBPROCESS_TIMEOUT_SEC = 2
MIN_DISK_SIZE_BYTES = 536870912000  # 500GB minimum for disk reporting
BYTES_PER_GB = 1073741824
BYTES_PER_TB = 1099511627776


def get_cpu_info() -> dict[str, Any]:
    """Gather CPU information from /proc/cpuinfo"""
    cpu_info: dict[str, Any] = {}
    if not os.path.exists('/proc/cpuinfo'):
        return cpu_info

    with open('/proc/cpuinfo', 'r') as f:
        cpuinfo = f.read()

    # Extract CPU model
    for line in cpuinfo.split('\n'):
        if 'model name' in line:
            cpu_info["model"] = line.split(':')[1].strip()
            break

    # Count physical and logical processors
    cpu_info["logical_processors"] = os.cpu_count()

    # Count physical processors (unique physical IDs)
    physical_ids = set()
    for line in cpuinfo.split('\n'):
        if 'physical id' in line:
            physical_ids.add(line.split(':')[1].strip())
    cpu_info["physical_processors"] = len(physical_ids) if physical_ids else 1

    # Get CPU frequency
    try:
        with open('/proc/cpuinfo', 'r') as f:
            for line in f:
                if 'cpu MHz' in line:
                    cpu_info["speed_mhz"] = float(line.split(':')[1].strip())
                    break
    except (IOError, ValueError, IndexError):
        pass

    return cpu_info


def get_ram_info(wsl_version: Optional[str] = None) -> dict[str, Any]:
    """Gather RAM information from Windows (if WSL2) or /proc/meminfo and dmidecode

    Args:
        wsl_version: WSL version string (WSL1, WSL2) or None

    Returns:
        Dictionary with RAM information
    """
    ram_info: dict[str, Any] = {}

    # If running in WSL2, query Windows directly for accurate RAM info
    if wsl_version == "WSL2":
        try:
            # Get physical memory information from Windows
            ps_script = """
            $memory = Get-CimInstance Win32_PhysicalMemory
            $totalCapacityGB = ($memory | Measure-Object -Property Capacity -Sum).Sum / 1GB

            # Get memory details from first module (they're usually identical)
            $firstModule = $memory | Select-Object -First 1
            $speed = $firstModule.Speed
            $memoryType = switch ($firstModule.SMBIOSMemoryType) {
                20 { "DDR" }
                21 { "DDR2" }
                24 { "DDR3" }
                26 { "DDR4" }
                34 { "DDR5" }
                default { "Unknown" }
            }
            $manufacturer = $firstModule.Manufacturer

            Write-Output "$totalCapacityGB|$speed|$memoryType|$manufacturer"
            """

            result = subprocess.run(
                ['powershell.exe', '-Command', ps_script],
                capture_output=True, text=True, timeout=10
            )

            if result.returncode == 0 and result.stdout.strip():
                parts = result.stdout.strip().split('|')
                if len(parts) >= 4:
                    total_gb = float(parts[0])
                    speed_mhz = parts[1].strip()
                    memory_type = parts[2].strip()
                    manufacturer = parts[3].strip()

                    ram_info["total_gb"] = round(total_gb, 2)
                    if speed_mhz and speed_mhz != "0":
                        ram_info["speed"] = f"{speed_mhz} MHz"
                    if memory_type and memory_type != "Unknown":
                        ram_info["type"] = memory_type
                    if manufacturer and manufacturer.strip():
                        ram_info["manufacturer"] = manufacturer.strip()

                    # If we got Windows RAM info, return it
                    if ram_info:
                        return ram_info
        except Exception as e:
            # Fall through to Linux detection if Windows query fails
            print(f"Warning: Could not query Windows RAM info: {e}")

    # Fallback to Linux detection
    # Get total RAM from /proc/meminfo
    if os.path.exists('/proc/meminfo'):
        with open('/proc/meminfo', 'r') as f:
            meminfo = f.read()
            for line in meminfo.split('\n'):
                if 'MemTotal' in line:
                    # Convert from kB to GB
                    ram_kb = int(line.split(':')[1].strip().split()[0])
                    ram_info["total_gb"] = round(ram_kb / (1024 * 1024), 2)
                    break

    # Try to get RAM speed and type using dmidecode if available
    try:
        result = subprocess.run(['dmidecode', '-t', 'memory'],
                              capture_output=True, text=True, timeout=SUBPROCESS_TIMEOUT_SEC)
        if result.returncode == 0:
            output = result.stdout
            # Try to find speed
            for line in output.split('\n'):
                if 'Speed:' in line and 'MHz' in line:
                    speed_str = line.split(':')[1].strip()
                    if 'Unknown' not in speed_str and speed_str != 'MT/s':
                        ram_info["speed"] = speed_str
                        break

            # Try to find type (DDR3, DDR4, etc.)
            for line in output.split('\n'):
                if 'Type:' in line and 'DDR' in line:
                    type_str = line.split(':')[1].strip()
                    if type_str and 'Unknown' not in type_str:
                        ram_info["type"] = type_str
                        break
    except (subprocess.TimeoutExpired, FileNotFoundError, PermissionError):
        pass

    return ram_info


def get_disk_info(wsl_version: Optional[str] = None) -> list[dict[str, Any]]:
    """Gather disk information from Windows (if WSL2) or lsblk

    Args:
        wsl_version: WSL version string (WSL1, WSL2) or None for detecting disk type

    Returns:
        List of dictionaries with disk information
    """
    disks: list[dict[str, Any]] = []

    # If running in WSL2, query Windows directly for accurate disk info
    if wsl_version == "WSL2":
        try:
            # Get physical disk information from Windows
            ps_script = """
            Get-PhysicalDisk | ForEach-Object {
                $disk = $_
                $mediaType = switch ($disk.MediaType) {
                    'SSD' { 'SSD' }
                    'HDD' { 'HDD' }
                    'SCM' { 'SCM' }
                    default { 'Unknown' }
                }
                $busType = $disk.BusType
                $model = $disk.FriendlyName
                $sizeGB = [math]::Round($disk.Size / 1GB, 2)

                Write-Output "$mediaType|$busType|$model|$sizeGB"
            }
            """

            result = subprocess.run(
                ['powershell.exe', '-Command', ps_script],
                capture_output=True, text=True, timeout=10
            )

            if result.returncode == 0 and result.stdout.strip():
                for line in result.stdout.strip().split('\n'):
                    if '|' in line:
                        parts = line.strip().split('|')
                        if len(parts) >= 4:
                            media_type = parts[0]
                            bus_type = parts[1]
                            model = parts[2]
                            size_gb = float(parts[3])

                            # Skip virtual disks (WSL2 creates these)
                            if 'msft virtual disk' in model.lower() or 'virtual disk' in model.lower():
                                continue

                            # Skip disks smaller than 500GB
                            if size_gb < (MIN_DISK_SIZE_BYTES / BYTES_PER_GB):
                                continue

                            # Add bus type info to media type for NVMe drives
                            if 'NVMe' in bus_type or 'nvme' in bus_type.lower():
                                disk_type = f"{media_type} (NVMe)"
                            elif 'SATA' in bus_type:
                                disk_type = f"{media_type} (SATA)"
                            else:
                                disk_type = media_type

                            # Convert size to human readable
                            if size_gb >= 1000:
                                size_str = f"{size_gb / 1000:.1f}T"
                            else:
                                size_str = f"{size_gb:.1f}G"

                            disk_info: dict[str, str] = {
                                "name": model,
                                "size": size_str,
                                "type": disk_type,
                                "model": model
                            }
                            disks.append(disk_info)

                # If we got Windows disk info, return it
                if disks:
                    return disks
        except Exception as e:
            # Fall through to Linux detection if Windows query fails
            print(f"Warning: Could not query Windows disk info: {e}")

    # Fallback to lsblk for native Linux or if Windows query failed
    try:
        result = subprocess.run(['lsblk', '-d', '-b', '-J', '-o', 'NAME,TYPE,SIZE,ROTA,MODEL'],
                              capture_output=True, text=True, timeout=SUBPROCESS_TIMEOUT_SEC)
        if result.returncode == 0:
            lsblk_data = json.loads(result.stdout)
            for device in lsblk_data.get('blockdevices', []):
                name = device.get('name', '')
                size_bytes = device.get('size', 0)

                # Only include actual physical disks (exclude loop, ram, small utility partitions)
                # Filter: must be 'disk' type, not loop/ram, and >= 500GB in size
                if (device.get('type') == 'disk' and
                    not name.startswith(('loop', 'ram')) and
                    isinstance(size_bytes, (int, str))):

                    try:
                        size_bytes_int = int(size_bytes)
                        # Skip disks smaller than 500GB
                        if size_bytes_int < MIN_DISK_SIZE_BYTES:
                            continue

                        # Convert bytes to human readable
                        if size_bytes_int >= BYTES_PER_TB:
                            size_str = f"{size_bytes_int / BYTES_PER_TB:.1f}T"
                        else:  # GB
                            size_str = f"{size_bytes_int / BYTES_PER_GB:.1f}G"

                        # In WSL2, ROTA flag is unreliable for Virtual Disks, assume SSD
                        disk_type = "SSD"
                        if wsl_version != "WSL2":
                            disk_type = "HDD" if device.get('rota') else "SSD"

                        disk_info: dict[str, str] = {
                            "name": name,
                            "size": size_str,
                            "type": disk_type,
                        }
                        if device.get('model'):
                            disk_info["model"] = device.get('model')
                        disks.append(disk_info)
                    except (ValueError, TypeError):
                        pass
    except (subprocess.TimeoutExpired, FileNotFoundError, json.JSONDecodeError):
        pass

    return disks


def get_system_info() -> dict[str, Any]:
    """Gather system information"""
    system_info: dict[str, Any] = {
        "os": platform.system(),
        "os_release": platform.release(),
        "os_version": platform.version(),
    }

    # Check if running in WSL
    wsl_version = None
    if os.path.exists('/proc/version'):
        with open('/proc/version', 'r') as f:
            version_info = f.read().lower()
            if 'microsoft' in version_info or 'wsl' in version_info:
                # Try to determine WSL version
                if 'wsl2' in version_info or os.path.exists('/run/WSL'):
                    wsl_version = "WSL2"
                else:
                    wsl_version = "WSL1"

    if wsl_version:
        system_info["wsl_version"] = wsl_version

    # CPU information
    system_info["cpu"] = get_cpu_info()

    # RAM information
    system_info["ram"] = get_ram_info(wsl_version)

    # Disk information - only physical drives
    disks = get_disk_info(wsl_version)
    if disks:
        system_info["disks"] = disks

    return system_info
