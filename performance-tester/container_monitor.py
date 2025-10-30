#!/usr/bin/env python3
"""
Container monitoring utilities.

Monitors CPU and memory usage of Docker containers.
"""

import time
import threading
import subprocess
import json
from datetime import datetime


# Constants
SAMPLING_INTERVAL_MS = 100
SUBPROCESS_TIMEOUT_SEC = 5


class ContainerMonitor:
    """Monitor CPU and memory usage of a Docker container"""

    def __init__(self, container_name: str, sampling_interval_ms: int = SAMPLING_INTERVAL_MS):
        self.container_name = container_name
        self.sampling_interval = sampling_interval_ms / 1000.0  # Convert to seconds
        self.samples = []
        self.monitoring = False
        self.monitor_thread: threading.Thread | None = None
        self.start_time: datetime | None = None
        self.container_id: str | None = None

    def check_container_exists(self) -> bool:
        """Check if container exists and is running"""
        try:
            result = subprocess.run(
                ['docker', 'inspect', '--format', '{{.Id}}', self.container_name],
                capture_output=True, text=True, timeout=SUBPROCESS_TIMEOUT_SEC
            )
            if result.returncode == 0 and result.stdout.strip():
                self.container_id = result.stdout.strip()[:12]  # Short ID
                return True
            return False
        except (subprocess.TimeoutExpired, FileNotFoundError):
            return False

    def start_monitoring(self) -> bool:
        """Start monitoring the container"""
        if not self.check_container_exists():
            print(f"WARNING: Container '{self.container_name}' not found or not running")
            return False

        print(f"Found container: {self.container_name} (ID: {self.container_id})")

        self.start_time = datetime.now()
        self.monitoring = True
        self.monitor_thread = threading.Thread(target=self._monitor_loop, daemon=True)
        self.monitor_thread.start()
        print(f"Started monitoring {self.container_name} every {int(self.sampling_interval * 1000)}ms")
        return True

    def _get_container_stats(self) -> dict | None:
        """Get current container statistics using docker stats"""
        try:
            # Use docker stats --no-stream to get a single snapshot
            result = subprocess.run(
                ['docker', 'stats', '--no-stream', '--format',
                 '{{.CPUPerc}},{{.MemUsage}}', self.container_name],
                capture_output=True, text=True, timeout=SUBPROCESS_TIMEOUT_SEC
            )

            if result.returncode != 0 or not result.stdout.strip():
                return None

            # Parse output: "0.50%,123.4MiB / 1.5GiB"
            parts = result.stdout.strip().split(',')
            if len(parts) != 2:
                return None

            # Parse CPU percentage
            cpu_str = parts[0].strip().rstrip('%')
            cpu_percent = float(cpu_str)

            # Parse memory usage (extract used memory in MB)
            mem_str = parts[1].strip()
            # Format: "123.4MiB / 1.5GiB" or "1.2GiB / 8GiB"
            mem_used_str = mem_str.split('/')[0].strip()

            # Convert to MB
            if 'GiB' in mem_used_str:
                mem_mb = float(mem_used_str.replace('GiB', '')) * 1024
            elif 'MiB' in mem_used_str:
                mem_mb = float(mem_used_str.replace('MiB', ''))
            elif 'KiB' in mem_used_str:
                mem_mb = float(mem_used_str.replace('KiB', '')) / 1024
            else:
                # Try to handle 'B' or unknown formats
                mem_mb = 0.0

            return {
                'cpu_percent': cpu_percent,
                'memory_mb': mem_mb
            }

        except (subprocess.TimeoutExpired, ValueError, IndexError) as e:
            print(f"Error getting container stats: {e}")
            return None

    def _monitor_loop(self):
        """Background monitoring loop"""
        while self.monitoring:
            try:
                stats = self._get_container_stats()
                if stats is None:
                    # Container might have stopped
                    time.sleep(self.sampling_interval)
                    continue

                sample = {
                    "timestamp": datetime.now().isoformat(),
                    "cpu_percent": round(stats['cpu_percent'], 2),
                    "memory_mb": round(stats['memory_mb'], 2)
                }
                self.samples.append(sample)

                time.sleep(self.sampling_interval)

            except Exception as e:
                print(f"Error monitoring container: {e}")
                break

    def stop_monitoring(self):
        """Stop monitoring and return results"""
        self.monitoring = False
        if self.monitor_thread is not None:
            self.monitor_thread.join(timeout=2)

        if not self.samples:
            print(f"No monitoring samples collected for {self.container_name}")
            return None

        # Calculate summary statistics
        cpu_values = [s["cpu_percent"] for s in self.samples]
        mem_values = [s["memory_mb"] for s in self.samples]

        summary = {
            "avg_cpu_percent": round(sum(cpu_values) / len(cpu_values), 2),
            "peak_cpu_percent": round(max(cpu_values), 2),
            "avg_memory_mb": round(sum(mem_values) / len(mem_values), 2),
            "peak_memory_mb": round(max(mem_values), 2),
            "total_samples": len(self.samples)
        }

        print(f"Monitoring stopped for {self.container_name}. Collected {len(self.samples)} samples")
        print(f"  Avg CPU: {summary['avg_cpu_percent']}%, Peak: {summary['peak_cpu_percent']}%")
        print(f"  Avg RAM: {summary['avg_memory_mb']} MB, Peak: {summary['peak_memory_mb']} MB")

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
            "container_info": {
                "name": self.container_name,
                "id": self.container_id
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

        print(f"Container metrics saved to: {filename}")
        return True
