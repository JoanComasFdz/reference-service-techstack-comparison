#!/usr/bin/env python3
"""
Process monitoring utilities.

Monitors CPU and memory usage of processes running on specific ports.
"""

import os
import time
import threading
import subprocess
import psutil
import json
from datetime import datetime


# Constants
SAMPLING_INTERVAL_MS = 100
SUBPROCESS_TIMEOUT_SEC = 2


class ProcessMonitor:
    """Monitor CPU and memory usage of a process running on a specific port"""

    def __init__(self, port: int, sampling_interval_ms: int = SAMPLING_INTERVAL_MS):
        self.port = port
        self.sampling_interval = sampling_interval_ms / 1000.0  # Convert to seconds
        self.samples = []
        self.process: psutil.Process | None = None
        self.pid: int | None = None
        self.process_name: str | None = None
        self.monitoring = False
        self.monitor_thread: threading.Thread | None = None
        self.start_time: datetime | None = None

    def find_process_on_port(self) -> int | None:
        """Find PID of process listening on the specified port"""
        try:
            # Method 1: Try using lsof (faster)
            result = subprocess.run(['lsof', '-ti', f':{self.port}'],
                                  capture_output=True, text=True, timeout=SUBPROCESS_TIMEOUT_SEC)
            if result.returncode == 0 and result.stdout.strip():
                return int(result.stdout.strip().split('\n')[0])
        except (subprocess.TimeoutExpired, FileNotFoundError, ValueError):
            pass

        # Method 2: Use psutil to find process by port
        try:
            for conn in psutil.net_connections(kind='inet'):
                if conn.laddr and conn.laddr.port == self.port and conn.status == 'LISTEN':
                    return conn.pid
        except (psutil.AccessDenied, psutil.NoSuchProcess):
            pass

        return None

    def start_monitoring(self) -> bool:
        """Start monitoring the process"""
        self.pid = self.find_process_on_port()
        if self.pid is None:
            print(f"WARNING: No process found listening on port {self.port}")
            return False

        try:
            self.process = psutil.Process(self.pid)
            # Get more descriptive process name including executable
            base_name = self.process.name()

            # Try to get the executable name from cmdline
            try:
                cmdline = self.process.cmdline()
                if cmdline and len(cmdline) > 0:
                    # For java processes, look for -jar argument
                    if base_name.lower() in ['java', 'java.exe']:
                        for i, arg in enumerate(cmdline):
                            if arg == '-jar' and i + 1 < len(cmdline):
                                jar_path = cmdline[i + 1]
                                jar_name = os.path.basename(jar_path)
                                # Remove .jar extension to get clean process name
                                if jar_name.endswith('.jar'):
                                    self.process_name = jar_name[:-4]
                                else:
                                    self.process_name = jar_name
                                break
                        else:
                            # No -jar found, just use java
                            self.process_name = base_name
                    # For dotnet processes, get the dll/exe name
                    elif base_name.lower() in ['dotnet', 'dotnet.exe']:
                        # Look for the dll or exe being run
                        for arg in cmdline[1:]:  # Skip the 'dotnet' command itself
                            if arg.endswith('.dll') or arg.endswith('.exe'):
                                dll_name = os.path.basename(arg)
                                # Remove .dll or .exe extension to get clean process name
                                if dll_name.endswith('.dll'):
                                    self.process_name = dll_name[:-4]
                                elif dll_name.endswith('.exe'):
                                    self.process_name = dll_name[:-4]
                                else:
                                    self.process_name = dll_name
                                break
                        else:
                            self.process_name = base_name
                    # For python processes, get the script name
                    elif base_name.lower() in ['python', 'python3', 'python.exe', 'python3.exe']:
                        # Look for the Python script being run
                        for arg in cmdline[1:]:  # Skip the 'python' command itself
                            if not arg.startswith('-') and not arg.endswith(('/python', '/python3', '\\python', '\\python3')):
                                script_name = os.path.basename(arg)
                                self.process_name = script_name
                                break
                        else:
                            self.process_name = base_name
                    else:
                        # For other processes, just use the base name
                        self.process_name = base_name
                else:
                    self.process_name = base_name
            except (psutil.AccessDenied, psutil.NoSuchProcess):
                # Fall back to basic name if cmdline parsing fails
                self.process_name = base_name

            print(f"Found process on port {self.port}: {self.process_name} (PID: {self.pid})")
        except psutil.NoSuchProcess:
            print(f"WARNING: Process {self.pid} no longer exists")
            return False

        # Initialize cpu_percent() to enable non-blocking mode in monitoring loop
        self.process.cpu_percent(interval=None)

        self.start_time = datetime.now()
        self.monitoring = True
        self.monitor_thread = threading.Thread(target=self._monitor_loop, daemon=True)
        self.monitor_thread.start()
        print(f"Started monitoring {self.process_name} (PID: {self.pid}) every {int(self.sampling_interval * 1000)}ms")
        return True

    def _monitor_loop(self):
        """Background monitoring loop"""
        while self.monitoring and self.process is not None:
            try:
                # Get current metrics (non-blocking since initialized with interval=None)
                cpu_percent = self.process.cpu_percent(interval=None)
                memory_info = self.process.memory_info()
                num_threads = self.process.num_threads()

                sample = {
                    "timestamp": datetime.now().isoformat(),
                    "cpu_percent": round(cpu_percent, 2),
                    "memory_rss_mb": round(memory_info.rss / (1024 * 1024), 2),
                    "threads": num_threads
                }
                self.samples.append(sample)

                time.sleep(self.sampling_interval)
            except psutil.NoSuchProcess:
                print(f"Process {self.pid} terminated during monitoring")
                break
            except Exception as e:
                print(f"Error monitoring process: {e}")
                break

    def stop_monitoring(self):
        """Stop monitoring and return results"""
        self.monitoring = False
        if self.monitor_thread is not None:
            self.monitor_thread.join(timeout=2)

        if not self.samples:
            print("No monitoring samples collected")
            return None

        # Calculate summary statistics
        cpu_values = [s["cpu_percent"] for s in self.samples]
        rss_values = [s["memory_rss_mb"] for s in self.samples]

        summary = {
            "avg_cpu_percent": round(sum(cpu_values) / len(cpu_values), 2),
            "peak_cpu_percent": round(max(cpu_values), 2),
            "avg_memory_rss_mb": round(sum(rss_values) / len(rss_values), 2),
            "peak_memory_rss_mb": round(max(rss_values), 2),
            "total_samples": len(self.samples)
        }

        print(f"Monitoring stopped. Collected {len(self.samples)} samples")
        print(f"  Avg CPU: {summary['avg_cpu_percent']}%, Peak: {summary['peak_cpu_percent']}%")
        print(f"  Avg RAM: {summary['avg_memory_rss_mb']} MB, Peak: {summary['peak_memory_rss_mb']} MB")

        return summary

    def get_report_data(self) -> dict | None:
        """Get monitoring data for report generation

        Returns:
            Dictionary with monitoring data or None if no samples
        """
        summary = self.stop_monitoring()
        if summary is None:
            return None

        return {
            "test_date": self.start_time.strftime('%Y-%m-%d %H:%M:%S') if self.start_time else None,
            "process_info": {
                "pid": self.pid,
                "name": self.process_name,
                "port": self.port
            },
            "sampling_interval_ms": int(self.sampling_interval * 1000),
            "samples": self.samples,
            "summary": summary
        }

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

        print(f"Resource metrics saved to: {filename}")
        return True
