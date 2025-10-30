#!/usr/bin/env python3
"""
System-wide monitoring utilities.

Monitors overall CPU and memory usage of the entire system.
"""

import time
import threading
import psutil
import json
import subprocess
import os
from datetime import datetime


# Constants
SAMPLING_INTERVAL_MS = 500


def _is_wsl2() -> bool:
    """Detect if running in WSL2 environment

    Returns:
        True if running in WSL2, False otherwise
    """
    try:
        # Check for WSL in /proc/version
        if os.path.exists('/proc/version'):
            with open('/proc/version', 'r') as f:
                version_info = f.read().lower()
                return 'microsoft' in version_info or 'wsl' in version_info
    except Exception:
        pass
    return False


def _get_windows_total_ram() -> int | None:
    """Query Windows host total physical RAM via PowerShell

    Returns:
        Total RAM in bytes, or None if query fails
    """
    try:
        result = subprocess.run(
            ['powershell.exe', '-Command',
             'Get-CimInstance Win32_ComputerSystem | Select-Object -ExpandProperty TotalPhysicalMemory'],
            capture_output=True,
            text=True,
            timeout=5
        )
        if result.returncode == 0:
            total_bytes = int(result.stdout.strip())
            return total_bytes
    except Exception:
        pass
    return None


def _get_windows_memory_usage() -> tuple[int, int] | None:
    """Query Windows host memory usage (total and free) via PowerShell

    Returns:
        Tuple of (total_mb, used_mb), or None if query fails
    """
    try:
        # Query Windows for memory info (values returned in KB)
        result = subprocess.run(
            ['powershell.exe', '-Command',
             '$os = Get-CimInstance Win32_OperatingSystem; ' +
             'Write-Output "$($os.TotalVisibleMemorySize),$($os.FreePhysicalMemory)"'],
            capture_output=True,
            text=True,
            timeout=5
        )
        if result.returncode == 0:
            output = result.stdout.strip()
            total_kb, free_kb = map(int, output.split(','))

            # Convert KB to MB
            total_mb = total_kb / 1024.0
            free_mb = free_kb / 1024.0
            used_mb = total_mb - free_mb

            return (total_mb, used_mb)
    except Exception:
        pass
    return None


class SystemMonitor:
    """Monitor overall system CPU and memory usage"""

    def __init__(self, sampling_interval_ms: int = SAMPLING_INTERVAL_MS):
        self.sampling_interval = sampling_interval_ms / 1000.0  # Convert to seconds
        self.samples = []
        self.monitoring = False
        self.monitor_thread: threading.Thread | None = None
        self.start_time: datetime | None = None
        self.cpu_count = psutil.cpu_count(logical=True)  # Get number of logical CPUs

        # Detect WSL2 and get Windows host RAM if available
        self.is_wsl2 = _is_wsl2()
        self.windows_total_ram_mb = None

        if self.is_wsl2:
            windows_ram_bytes = _get_windows_total_ram()
            if windows_ram_bytes:
                self.windows_total_ram_mb = windows_ram_bytes / (1024 * 1024)
                print(f"  Detected WSL2 environment")
                print(f"  Windows host total RAM: {self.windows_total_ram_mb:.0f} MB")

    def start_monitoring(self) -> bool:
        """Start monitoring the system"""
        # Initialize cpu_percent() to enable non-blocking mode in monitoring loop
        psutil.cpu_percent(interval=None)

        self.start_time = datetime.now()
        self.monitoring = True
        self.monitor_thread = threading.Thread(target=self._monitor_loop, daemon=True)
        self.monitor_thread.start()
        print(f"Started monitoring overall system every {int(self.sampling_interval * 1000)}ms")
        print(f"  System has {self.cpu_count} logical CPU(s)")
        return True

    def _monitor_loop(self):
        """Background monitoring loop"""
        while self.monitoring:
            try:
                # Get overall system CPU and memory (non-blocking since initialized with interval=None)
                cpu_percent = psutil.cpu_percent(interval=None)

                # Get memory info
                if self.is_wsl2:
                    # Query Windows host memory directly for accurate usage
                    windows_mem = _get_windows_memory_usage()
                    if windows_mem:
                        memory_total_mb, memory_used_mb = windows_mem
                        memory_percent = (memory_used_mb / memory_total_mb) * 100 if memory_total_mb > 0 else 0
                    else:
                        # Fallback to psutil if Windows query fails
                        mem = psutil.virtual_memory()
                        memory_used_mb = (mem.total - mem.available) / (1024 * 1024)
                        memory_total_mb = self.windows_total_ram_mb if self.windows_total_ram_mb else mem.total / (1024 * 1024)
                        memory_percent = (memory_used_mb / memory_total_mb) * 100 if memory_total_mb > 0 else 0
                else:
                    # Native Linux: use psutil
                    mem = psutil.virtual_memory()
                    memory_used_mb = (mem.total - mem.available) / (1024 * 1024)
                    memory_total_mb = mem.total / (1024 * 1024)
                    memory_percent = mem.percent

                sample = {
                    "timestamp": datetime.now().isoformat(),
                    "cpu_percent": round(cpu_percent, 2),
                    "memory_used_mb": round(memory_used_mb, 2),
                    "memory_total_mb": round(memory_total_mb, 2),
                    "memory_percent": round(memory_percent, 2)
                }
                self.samples.append(sample)

                time.sleep(self.sampling_interval)

            except Exception as e:
                print(f"Error monitoring system: {e}")
                break

    def stop_monitoring(self):
        """Stop monitoring and return results"""
        self.monitoring = False
        if self.monitor_thread is not None:
            self.monitor_thread.join(timeout=2)

        if not self.samples:
            print("No system monitoring samples collected")
            return None

        # Calculate summary statistics
        cpu_values = [s["cpu_percent"] for s in self.samples]
        mem_values = [s["memory_used_mb"] for s in self.samples]
        mem_percent_values = [s["memory_percent"] for s in self.samples]

        summary = {
            "avg_cpu_percent": round(sum(cpu_values) / len(cpu_values), 2),
            "peak_cpu_percent": round(max(cpu_values), 2),
            "min_cpu_percent": round(min(cpu_values), 2),
            "avg_memory_used_mb": round(sum(mem_values) / len(mem_values), 2),
            "peak_memory_used_mb": round(max(mem_values), 2),
            "avg_memory_percent": round(sum(mem_percent_values) / len(mem_percent_values), 2),
            "peak_memory_percent": round(max(mem_percent_values), 2),
            "total_samples": len(self.samples)
        }

        print(f"System monitoring stopped. Collected {len(self.samples)} samples")
        print(f"  Avg CPU: {summary['avg_cpu_percent']}%, Peak: {summary['peak_cpu_percent']}%")
        print(f"  Avg RAM: {summary['avg_memory_used_mb']:.0f} MB ({summary['avg_memory_percent']}%), "
              f"Peak: {summary['peak_memory_used_mb']:.0f} MB ({summary['peak_memory_percent']}%)")

        return summary

    def get_report_data(self) -> dict | None:
        """Get monitoring data for report generation

        Returns:
            Dictionary with monitoring data or None if no samples
        """
        summary = self.stop_monitoring()
        if summary is None:
            return None

        report = {
            "test_date": self.start_time.strftime('%Y-%m-%d %H:%M:%S') if self.start_time else None,
            "cpu_count": self.cpu_count,
            "sampling_interval_ms": int(self.sampling_interval * 1000),
            "samples": self.samples,
            "summary": summary
        }

        # Add WSL2 information if applicable
        if self.is_wsl2:
            report["is_wsl2"] = True
            if self.windows_total_ram_mb:
                report["windows_host_total_ram_mb"] = round(self.windows_total_ram_mb, 2)

        return report

    def save_report(self, filename: str) -> bool:
        """Save monitoring data to JSON file

        Args:
            filename: Full path to output file

        Returns:
            True if saved successfully, False otherwise
        """
        report_data = self.get_report_data()
        if report_data is None:
            return False

        with open(filename, 'w') as f:
            json.dump(report_data, f, indent=2)

        print(f"System metrics saved to: {filename}")
        return True
