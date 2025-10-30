#!/usr/bin/env python3
"""
compare_test_results.py

Reads test result JSON files from a folder and generates a comprehensive
markdown comparison report highlighting differences in performance metrics
across different test runs.
"""

import json
import sys
import argparse
import platform
from pathlib import Path
from datetime import datetime
from typing import Any
import re


class TestRun:
    """Represents a single test run with all its metrics."""

    def __init__(self, timestamp: str):
        self.timestamp = timestamp
        self.main_report = None
        self.resource_metrics = None
        self.api_throughput = None
        self.system_metrics = None
        self.events_throughput = None

    def load_main_report(self, file_path: Path):
        """Load the main test report JSON."""
        with open(file_path, 'r') as f:
            self.main_report = json.load(f)

    def load_resource_metrics(self, file_path: Path):
        """Load resource metrics JSON."""
        with open(file_path, 'r') as f:
            self.resource_metrics = json.load(f)

    def load_api_throughput(self, file_path: Path):
        """Load API throughput JSON."""
        with open(file_path, 'r') as f:
            self.api_throughput = json.load(f)

    def load_system_metrics(self, file_path: Path):
        """Load system metrics JSON."""
        with open(file_path, 'r') as f:
            self.system_metrics = json.load(f)

    def load_events_throughput(self, file_path: Path):
        """Load events throughput JSON."""
        with open(file_path, 'r') as f:
            self.events_throughput = json.load(f)

    def get_test_date(self) -> str:
        """Get the test date from main report."""
        if self.main_report:
            return self.main_report.get('test_date', 'N/A')
        return 'N/A'

    def get_process_name(self) -> str:
        """Get the monitored process name."""
        if self.main_report:
            return self.main_report.get('monitored_process', {}).get('name', 'N/A')
        return 'N/A'

    def get_total_runtime(self) -> float:
        """Get total runtime in seconds."""
        if self.main_report:
            return self.main_report.get('total_runtime_seconds', 0)
        return 0

    def get_config(self) -> dict[str, Any]:
        """Get test configuration."""
        if self.main_report:
            return self.main_report.get('configuration', {})
        return {}

    def get_phase_results(self) -> dict[str, Any]:
        """Get phase results (publish, consume, API)."""
        if self.main_report:
            return self.main_report.get('results', {})
        return {}

    def get_resource_summary(self) -> dict[str, float]:
        """Get resource usage summary."""
        if self.resource_metrics:
            return self.resource_metrics.get('summary', {})
        return {}

    def get_api_summary(self) -> dict[str, float]:
        """Get API throughput summary."""
        if self.api_throughput:
            return self.api_throughput.get('summary', {})
        return {}

    def get_api_min_calls_filtered(self) -> float | None:
        """Get minimum API calls/s, excluding zero values (post-test measurements)."""
        if self.api_throughput and 'samples' in self.api_throughput:
            samples = self.api_throughput['samples']
            non_zero_rates = [s['calls_per_second'] for s in samples if s.get('calls_per_second', 0) > 0]
            return min(non_zero_rates) if non_zero_rates else 0
        # Fall back to summary if samples not available
        return self.get_api_summary().get('min_calls_per_second')

    def get_events_summary(self) -> dict[str, float]:
        """Get events throughput summary."""
        if self.events_throughput:
            return self.events_throughput.get('summary', {})
        return {}

    def get_system_summary(self) -> dict[str, float]:
        """Get system metrics summary."""
        if self.system_metrics:
            return self.system_metrics.get('summary', {})
        return {}

    def get_system_info(self) -> dict[str, Any]:
        """Get system information."""
        if self.main_report:
            return self.main_report.get('system', {})
        return {}


def discover_test_runs(folder_path: Path) -> dict[str, TestRun]:
    """
    Discover and group test result files by test run.

    Returns a dictionary mapping timestamp to TestRun objects.
    """
    test_runs = {}

    # Pattern to extract timestamp from filename
    # Format: test-report-{timestamp}-{process_name}.{type}.json
    pattern = re.compile(r'test-report-(\d{8}_\d{6})-(.+?)(?:\.json|\.)')

    # Scan all JSON files in the folder
    for file_path in folder_path.glob('*.json'):
        match = pattern.match(file_path.name)
        if not match:
            continue

        timestamp = match.group(1)
        process_name = match.group(2)
        base_name = f"test-report-{timestamp}-{process_name}"

        # Create or get TestRun object
        if timestamp not in test_runs:
            test_runs[timestamp] = TestRun(timestamp)

        test_run = test_runs[timestamp]

        # Determine file type and load
        try:
            if file_path.name == f"{base_name}.json":
                test_run.load_main_report(file_path)
            elif file_path.name == f"{base_name}.resource-metrics.json":
                test_run.load_resource_metrics(file_path)
            elif file_path.name == f"{base_name}.api-throughput.json":
                test_run.load_api_throughput(file_path)
            elif file_path.name == f"{base_name}.system-metrics.json":
                test_run.load_system_metrics(file_path)
            elif file_path.name == f"{base_name}.events-throughput.json":
                test_run.load_events_throughput(file_path)
        except Exception as e:
            print(f"Warning: Failed to load {file_path.name}: {e}", file=sys.stderr)

    return test_runs


def format_number(value: float | None, decimals: int = 2) -> str:
    """Format a number with specified decimals, handling None."""
    if value is None:
        return 'N/A'
    return f"{value:.{decimals}f}"


def get_top_3(data_list: list[dict], key: str, higher_is_better: bool = True) -> tuple[list[Any], list[Any], list[Any]]:
    """
    Get the top 3 values from a list of dictionaries.

    Args:
        data_list: List of dictionaries containing the data
        key: The key to sort by
        higher_is_better: If True, larger values are better; if False, smaller values are better

    Returns:
        Tuple of (first_place_values, second_place_values, third_place_values)
    """
    # Filter out None values
    valid_data = [d for d in data_list if d.get(key) is not None]

    if not valid_data:
        return ([], [], [])

    # Sort by the key
    sorted_data = sorted(valid_data, key=lambda x: x[key], reverse=higher_is_better)

    # Get unique values (top 3 distinct values)
    unique_values = []
    for item in sorted_data:
        val = item[key]
        if val not in unique_values:
            unique_values.append(val)
        if len(unique_values) == 3:
            break

    # Assign medals
    first = unique_values[0] if len(unique_values) > 0 else None
    second = unique_values[1] if len(unique_values) > 1 else None
    third = unique_values[2] if len(unique_values) > 2 else None

    return (first, second, third)


def add_medal(value_str: str, value: float | None, first: Any, second: Any, third: Any) -> str:
    """Add medal emoji to value string if it matches a top 3 value."""
    if value is None or value_str == 'N/A':
        return value_str

    if value == first:
        return f"{value_str} 🥇"
    elif value == second:
        return f"{value_str} 🥈"
    elif value == third:
        return f"{value_str} 🥉"
    else:
        return value_str


def parse_timestamp(timestamp_str: str) -> datetime:
    """Parse timestamp string in format YYYYMMDD_HHMMSS."""
    return datetime.strptime(timestamp_str, '%Y%m%d_%H%M%S')


def format_duration(seconds: float) -> str:
    """Format duration in seconds to human-readable string."""
    hours = int(seconds // 3600)
    minutes = int((seconds % 3600) // 60)
    secs = int(seconds % 60)

    if hours > 0:
        return f"{hours}h {minutes}m {secs}s"
    elif minutes > 0:
        return f"{minutes}m {secs}s"
    else:
        return f"{secs}s"


def generate_comparison_report(test_runs: dict[str, TestRun]) -> str:
    """Generate markdown comparison report."""

    if not test_runs:
        return "# Test Results Comparison\n\nNo test results found."

    # Sort test runs by timestamp (newest first)
    sorted_runs = sorted(test_runs.items(), key=lambda x: x[0], reverse=True)

    # Get test run time information
    timestamps = [parse_timestamp(ts) for ts, _ in sorted_runs]
    start_time = min(timestamps)
    end_time = max(timestamps)
    total_duration = (end_time - start_time).total_seconds()

    # Get system information from any test run (they should all be the same)
    system_info = {}
    for _, test_run in sorted_runs:
        system_info = test_run.get_system_info()
        if system_info:
            break

    # Start building the report
    report = []
    report.append("# Test Results Comparison Report")
    report.append("")
    report.append(f"**Generated:** {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    report.append("")

    # Hardware and Test Information
    report.append("## Test Environment")
    report.append("")
    report.append("**Hardware & System:**")

    # Extract system information from JSON
    cpu_model = "Unknown"
    cpu_count = "N/A"
    total_memory_gb = "N/A"
    ram_type = ""
    ram_speed = ""
    ram_manufacturer = ""
    platform_info = "N/A"
    python_version = "N/A"
    disk_info = []
    is_wsl = False

    if system_info:
        # Check if WSL
        is_wsl = 'wsl_version' in system_info

        # CPU info
        if 'cpu' in system_info:
            cpu = system_info['cpu']
            cpu_model = cpu.get('model', 'Unknown')
            cpu_count = cpu.get('logical_processors', 'N/A')

        # RAM info
        if 'ram' in system_info:
            ram = system_info['ram']
            total_memory_gb = format_number(ram.get('total_gb', 0), 2)
            ram_type = ram.get('type', '')
            ram_speed = ram.get('speed', '')
            ram_manufacturer = ram.get('manufacturer', '')

        # Platform info
        os_name = system_info.get('os', '')
        os_release = system_info.get('os_release', '')
        platform_info = f"{os_name} {os_release}".strip()

        # Python version - extract from os_version or use default
        python_version = platform.python_version()

        # Disk info
        if 'disks' in system_info:
            disk_info = system_info['disks']

    report.append(f"- **CPU Model:** {cpu_model}")
    report.append(f"- **CPU Cores:** {cpu_count} cores")

    # Build memory description
    memory_desc = f"{total_memory_gb} GB"
    if ram_type:
        memory_desc += f" {ram_type}"
    if ram_speed:
        memory_desc += f" @ {ram_speed}"
    report.append(f"- **Total Memory:** {memory_desc}")
    if ram_manufacturer:
        report.append(f"- **Memory Manufacturer:** {ram_manufacturer}")

    report.append(f"- **Platform:** {platform_info}")
    report.append(f"- **Python Version:** {python_version}")
    report.append("")

    # Disk information
    if disk_info:
        if is_wsl:
            report.append(f"**Storage ({len(disk_info)} physical drive(s) on Windows host):**")
        else:
            report.append(f"**Storage ({len(disk_info)} drive(s)):**")
        report.append("")
        for idx, disk in enumerate(disk_info, 1):
            disk_name = disk.get('name', 'Unknown')
            disk_type = disk.get('type', 'Unknown')
            disk_model = disk.get('model', '')
            disk_size = disk.get('size', 'N/A')

            report.append(f"**Drive {idx}:** {disk_name}")
            report.append(f"- **Type:** {disk_type}")
            if disk_model:
                report.append(f"- **Model:** {disk_model}")
            report.append(f"- **Capacity:** {disk_size}")
            report.append("")
    else:
        report.append("**Storage:** Unable to retrieve disk information")
        report.append("")

    report.append("**Test Run Information:**")
    report.append(f"- **Start Time:** {start_time.strftime('%Y-%m-%d %H:%M:%S')}")
    report.append(f"- **End Time:** {end_time.strftime('%Y-%m-%d %H:%M:%S')}")
    report.append(f"- **Total Duration:** {format_duration(total_duration)}")
    report.append(f"- **Number of Services Tested:** {len(sorted_runs)}")
    report.append("")

    # Overview Table - Sort by runtime (lower is better)
    sorted_by_runtime = sorted(sorted_runs, key=lambda x: x[1].get_total_runtime() or float('inf'))

    # Prepare data for medal calculation
    runtime_data = []
    for timestamp, test_run in sorted_by_runtime:
        runtime_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'runtime': test_run.get_total_runtime() if test_run.get_total_runtime() > 0 else None
        })

    # Get top 3 runtimes (lower is better)
    first_runtime, second_runtime, third_runtime = get_top_3(runtime_data, 'runtime', higher_is_better=False)

    report.append("## Test Runs Overview (sorted by Runtime - lower is better)")
    report.append("")
    report.append("| # | Test Date | Process | Runtime (s) | Events | API Duration | Workers |")
    report.append("|---|-----------|---------|-------------|--------|--------------|---------|")

    for idx, data in enumerate(runtime_data, 1):
        test_run = data['test_run']
        config = test_run.get_config()
        runtime = data['runtime']
        runtime_str = format_number(runtime) if runtime is not None else 'N/A'

        # Add medal for runtime (lower is better)
        runtime_str = add_medal(runtime_str, runtime, first_runtime, second_runtime, third_runtime)

        report.append(
            f"| {idx} | {test_run.get_test_date()} | "
            f"{test_run.get_process_name()} | {runtime_str} | "
            f"{config.get('num_events', 'N/A')} | {config.get('api_duration', 'N/A')} | "
            f"{config.get('api_concurrent_workers', 'N/A')} |"
        )

    report.append("")

    # Throughput Comparison
    report.append("## Throughput Comparison")
    report.append("")

    # Events Throughput
    report.append("### Event Processing Throughput (sorted by Avg - higher is better)")
    report.append("")
    report.append("| # | Process | Avg (events/s) | Peak (events/s) | Min (events/s)* | Std Dev** | CV%*** |")
    report.append("|---|---------|----------------|-----------------|-----------------|-----------|--------|")

    # Collect all values
    events_data = []
    for timestamp, test_run in sorted_runs:
        phase_results = test_run.get_phase_results()
        consume_phase = phase_results.get('phase2_consume', {})
        events_summary = test_run.get_events_summary()

        avg_throughput = consume_phase.get('throughput_events_per_sec',
                                          events_summary.get('avg_events_per_second'))
        peak_throughput = events_summary.get('peak_events_per_second')
        min_throughput = events_summary.get('min_events_per_second')
        std_dev = events_summary.get('std_dev_events_per_second')
        cv = events_summary.get('cv_events_per_second')

        events_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'avg': avg_throughput,
            'peak': peak_throughput,
            'min': min_throughput,
            'std_dev': std_dev,
            'cv': cv
        })

    # Get top 3 for each metric
    first_avg, second_avg, third_avg = get_top_3(events_data, 'avg', higher_is_better=True)
    first_peak, second_peak, third_peak = get_top_3(events_data, 'peak', higher_is_better=True)
    first_min, second_min, third_min = get_top_3(events_data, 'min', higher_is_better=True)
    first_std_dev, second_std_dev, third_std_dev = get_top_3(events_data, 'std_dev', higher_is_better=False)
    first_cv, second_cv, third_cv = get_top_3(events_data, 'cv', higher_is_better=False)

    # Sort by average throughput descending (higher is better)
    events_data_sorted = sorted(events_data, key=lambda x: x['avg'] or 0, reverse=True)

    # Generate table with medals
    for idx, data in enumerate(events_data_sorted, 1):
        avg_str = format_number(data['avg'])
        peak_str = format_number(data['peak'])
        min_str = format_number(data['min'])
        std_dev_str = format_number(data['std_dev'])
        cv_str = format_number(data['cv'], 1)

        # Add medals
        avg_str = add_medal(avg_str, data['avg'], first_avg, second_avg, third_avg)
        peak_str = add_medal(peak_str, data['peak'], first_peak, second_peak, third_peak)
        min_str = add_medal(min_str, data['min'], first_min, second_min, third_min)
        std_dev_str = add_medal(std_dev_str, data['std_dev'], first_std_dev, second_std_dev, third_std_dev)
        cv_str = add_medal(cv_str, data['cv'], first_cv, second_cv, third_cv)

        report.append(
            f"| {idx} | {data['test_run'].get_process_name()} | {avg_str} | "
            f"{peak_str} | {min_str} | {std_dev_str} | {cv_str} |"
        )

    report.append("")
    report.append("*Min (events/s): Lowest throughput recorded during testing. Higher values indicate better worst-case performance.")
    report.append("")
    report.append("**Std Dev: Standard deviation measures throughput variability. Lower values indicate more consistent performance.")
    report.append("")
    report.append("***CV%: Coefficient of variation (std dev / mean × 100). Lower values indicate more stable relative performance.")
    report.append("")

    # API Throughput
    report.append("### API Throughput (sorted by Avg - higher is better)")
    report.append("")
    report.append("| # | Process | Total Requests | Avg (calls/s) | Peak (calls/s) | Min (calls/s)* | Std Dev** | CV%*** | Avg Response Time (ms) |")
    report.append("|---|---------|----------------|---------------|----------------|----------------|-----------|--------|------------------------|")

    # Collect all values
    api_data = []
    for timestamp, test_run in sorted_runs:
        phase_results = test_run.get_phase_results()
        api_phase = phase_results.get('phase3_api', {})
        api_summary = test_run.get_api_summary()

        total_requests = api_phase.get('total_requests')
        avg_calls_ps = api_phase.get('throughput_calls_per_sec',
                                     api_summary.get('avg_calls_per_second'))
        peak_calls_ps = api_summary.get('peak_calls_per_second')
        min_calls_ps = test_run.get_api_min_calls_filtered()
        std_dev = api_summary.get('std_dev_calls_per_second')
        cv = api_summary.get('cv_calls_per_second')
        avg_response_time = api_summary.get('avg_response_time_ms')

        api_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'total_requests': total_requests,
            'avg_calls': avg_calls_ps,
            'peak_calls': peak_calls_ps,
            'min_calls': min_calls_ps,
            'std_dev': std_dev,
            'cv': cv,
            'avg_response_time': avg_response_time
        })

    # Get top 3 for each metric
    first_total_req, second_total_req, third_total_req = get_top_3(api_data, 'total_requests', higher_is_better=True)
    first_avg_calls, second_avg_calls, third_avg_calls = get_top_3(api_data, 'avg_calls', higher_is_better=True)
    first_peak_calls, second_peak_calls, third_peak_calls = get_top_3(api_data, 'peak_calls', higher_is_better=True)
    first_min_calls, second_min_calls, third_min_calls = get_top_3(api_data, 'min_calls', higher_is_better=True)
    first_api_std_dev, second_api_std_dev, third_api_std_dev = get_top_3(api_data, 'std_dev', higher_is_better=False)
    first_api_cv, second_api_cv, third_api_cv = get_top_3(api_data, 'cv', higher_is_better=False)
    first_response_time, second_response_time, third_response_time = get_top_3(api_data, 'avg_response_time', higher_is_better=False)

    # Sort by average calls/s descending (higher is better)
    api_data_sorted = sorted(api_data, key=lambda x: x['avg_calls'] or 0, reverse=True)

    # Generate table with medals
    for idx, data in enumerate(api_data_sorted, 1):
        total_req_str = str(data['total_requests']) if data['total_requests'] else 'N/A'
        avg_calls_str = format_number(data['avg_calls'])
        peak_calls_str = format_number(data['peak_calls'])
        min_calls_str = format_number(data['min_calls'])
        std_dev_str = format_number(data['std_dev'])
        cv_str = format_number(data['cv'], 1)
        response_time_str = format_number(data['avg_response_time'], 3)

        # Add medals
        total_req_str = add_medal(total_req_str, data['total_requests'], first_total_req, second_total_req, third_total_req)
        avg_calls_str = add_medal(avg_calls_str, data['avg_calls'], first_avg_calls, second_avg_calls, third_avg_calls)
        peak_calls_str = add_medal(peak_calls_str, data['peak_calls'], first_peak_calls, second_peak_calls, third_peak_calls)
        min_calls_str = add_medal(min_calls_str, data['min_calls'], first_min_calls, second_min_calls, third_min_calls)
        std_dev_str = add_medal(std_dev_str, data['std_dev'], first_api_std_dev, second_api_std_dev, third_api_std_dev)
        cv_str = add_medal(cv_str, data['cv'], first_api_cv, second_api_cv, third_api_cv)
        response_time_str = add_medal(response_time_str, data['avg_response_time'], first_response_time, second_response_time, third_response_time)

        report.append(
            f"| {idx} | {data['test_run'].get_process_name()} | {total_req_str} | "
            f"{avg_calls_str} | {peak_calls_str} | {min_calls_str} | {std_dev_str} | {cv_str} | {response_time_str} |"
        )

    report.append("")
    report.append("*Min (calls/s): Lowest throughput recorded during testing. Higher values indicate better worst-case performance.")
    report.append("")
    report.append("**Std Dev: Standard deviation measures throughput variability. Lower values indicate more consistent performance.")
    report.append("")
    report.append("***CV%: Coefficient of variation (std dev / mean × 100). Lower values indicate more stable relative performance.")
    report.append("")

    # Resource Usage Comparison
    report.append("## Resource Usage Comparison")
    report.append("")

    # CPU Usage
    report.append("### Process CPU Usage (sorted by Avg CPU % - lower is better)")
    report.append("")
    report.append("| # | Process | Avg CPU % | Peak CPU % |")
    report.append("|---|---------|-----------|------------|")

    # Collect CPU data
    cpu_data = []
    for timestamp, test_run in sorted_runs:
        resource_summary = test_run.get_resource_summary()
        avg_cpu = resource_summary.get('avg_cpu_percent')
        peak_cpu = resource_summary.get('peak_cpu_percent')
        cpu_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'avg_cpu': avg_cpu,
            'peak_cpu': peak_cpu
        })

    # Get top 3 (lower is better)
    first_avg_cpu, second_avg_cpu, third_avg_cpu = get_top_3(cpu_data, 'avg_cpu', higher_is_better=False)
    first_peak_cpu, second_peak_cpu, third_peak_cpu = get_top_3(cpu_data, 'peak_cpu', higher_is_better=False)

    # Sort by average CPU ascending (lower is better)
    cpu_data_sorted = sorted(cpu_data, key=lambda x: x['avg_cpu'] or float('inf'))

    for idx, data in enumerate(cpu_data_sorted, 1):
        avg_cpu_str = format_number(data['avg_cpu'])
        peak_cpu_str = format_number(data['peak_cpu'])

        # Add medals
        avg_cpu_str = add_medal(avg_cpu_str, data['avg_cpu'], first_avg_cpu, second_avg_cpu, third_avg_cpu)
        peak_cpu_str = add_medal(peak_cpu_str, data['peak_cpu'], first_peak_cpu, second_peak_cpu, third_peak_cpu)

        report.append(
            f"| {idx} | {data['test_run'].get_process_name()} | {avg_cpu_str} | {peak_cpu_str} |"
        )

    report.append("")

    # Memory Usage
    report.append("### Process Memory Usage (sorted by Avg - lower is better)")
    report.append("")
    report.append("| # | Process | Avg Memory (MB) | Peak Memory (MB) |")
    report.append("|---|---------|-----------------|------------------|")

    # Collect memory data
    memory_data = []
    for timestamp, test_run in sorted_runs:
        resource_summary = test_run.get_resource_summary()
        avg_memory = resource_summary.get('avg_memory_rss_mb')
        peak_memory = resource_summary.get('peak_memory_rss_mb')
        memory_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'avg_memory': avg_memory,
            'peak_memory': peak_memory
        })

    # Get top 3 (lower is better)
    first_avg_memory, second_avg_memory, third_avg_memory = get_top_3(memory_data, 'avg_memory', higher_is_better=False)
    first_peak_memory, second_peak_memory, third_peak_memory = get_top_3(memory_data, 'peak_memory', higher_is_better=False)

    # Sort by average memory ascending (lower is better)
    memory_data_sorted = sorted(memory_data, key=lambda x: x['avg_memory'] or float('inf'))

    for idx, data in enumerate(memory_data_sorted, 1):
        avg_memory_str = format_number(data['avg_memory'])
        peak_memory_str = format_number(data['peak_memory'])

        # Add medals
        avg_memory_str = add_medal(avg_memory_str, data['avg_memory'], first_avg_memory, second_avg_memory, third_avg_memory)
        peak_memory_str = add_medal(peak_memory_str, data['peak_memory'], first_peak_memory, second_peak_memory, third_peak_memory)

        report.append(
            f"| {idx} | {data['test_run'].get_process_name()} | {avg_memory_str} | {peak_memory_str} |"
        )

    report.append("")

    # System-wide metrics
    report.append("### System-Wide Metrics (sorted by Avg System CPU % - lower is better)")
    report.append("")
    report.append("| # | Process | Avg System CPU % | Peak System CPU % | Avg System Memory (MB) |")
    report.append("|---|---------|------------------|-------------------|------------------------|")

    # Collect system-wide data
    system_data = []
    for timestamp, test_run in sorted_runs:
        system_summary = test_run.get_system_summary()
        avg_sys_cpu = system_summary.get('avg_cpu_percent')
        peak_sys_cpu = system_summary.get('peak_cpu_percent')
        avg_sys_mem = system_summary.get('avg_memory_used_mb')
        system_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'avg_sys_cpu': avg_sys_cpu,
            'peak_sys_cpu': peak_sys_cpu,
            'avg_sys_mem': avg_sys_mem
        })

    # Get top 3 (lower is better)
    first_avg_sys_cpu, second_avg_sys_cpu, third_avg_sys_cpu = get_top_3(system_data, 'avg_sys_cpu', higher_is_better=False)
    first_peak_sys_cpu, second_peak_sys_cpu, third_peak_sys_cpu = get_top_3(system_data, 'peak_sys_cpu', higher_is_better=False)
    first_avg_sys_mem, second_avg_sys_mem, third_avg_sys_mem = get_top_3(system_data, 'avg_sys_mem', higher_is_better=False)

    # Sort by average system CPU ascending (lower is better)
    system_data_sorted = sorted(system_data, key=lambda x: x['avg_sys_cpu'] or float('inf'))

    for idx, data in enumerate(system_data_sorted, 1):
        avg_sys_cpu_str = format_number(data['avg_sys_cpu'])
        peak_sys_cpu_str = format_number(data['peak_sys_cpu'])
        avg_sys_mem_str = format_number(data['avg_sys_mem'])

        # Add medals
        avg_sys_cpu_str = add_medal(avg_sys_cpu_str, data['avg_sys_cpu'], first_avg_sys_cpu, second_avg_sys_cpu, third_avg_sys_cpu)
        peak_sys_cpu_str = add_medal(peak_sys_cpu_str, data['peak_sys_cpu'], first_peak_sys_cpu, second_peak_sys_cpu, third_peak_sys_cpu)
        avg_sys_mem_str = add_medal(avg_sys_mem_str, data['avg_sys_mem'], first_avg_sys_mem, second_avg_sys_mem, third_avg_sys_mem)

        report.append(
            f"| {idx} | {data['test_run'].get_process_name()} | {avg_sys_cpu_str} | "
            f"{peak_sys_cpu_str} | {avg_sys_mem_str} |"
        )

    report.append("")

    # Performance Highlights
    report.append("## Performance Highlights")
    report.append("")

    # Find best/worst for various metrics
    runs_with_data = []
    for timestamp, test_run in sorted_runs:
        phase_results = test_run.get_phase_results()
        events_summary = test_run.get_events_summary()
        api_summary = test_run.get_api_summary()
        resource_summary = test_run.get_resource_summary()

        consume_phase = phase_results.get('phase2_consume', {})
        api_phase = phase_results.get('phase3_api', {})

        runs_with_data.append({
            'timestamp': timestamp,
            'test_run': test_run,
            'events_throughput': consume_phase.get('throughput_events_per_sec',
                                                  events_summary.get('avg_events_per_second', 0)),
            'events_cv': events_summary.get('cv_events_per_second', 0),
            'api_throughput': api_phase.get('throughput_calls_per_sec',
                                           api_summary.get('avg_calls_per_second', 0)),
            'api_cv': api_summary.get('cv_calls_per_second', 0),
            'avg_cpu': resource_summary.get('avg_cpu_percent', 0),
            'peak_memory': resource_summary.get('peak_memory_rss_mb', 0),
        })

    if runs_with_data:
        # Fastest event processing
        fastest_events = max(runs_with_data, key=lambda x: x['events_throughput'] or 0)
        report.append(f"- **Fastest Event Processing:** {fastest_events['test_run'].get_process_name()} - "
                     f"{format_number(fastest_events['events_throughput'])} events/s")

        # Fastest API
        fastest_api = max(runs_with_data, key=lambda x: x['api_throughput'] or 0)
        report.append(f"- **Fastest API Throughput:** {fastest_api['test_run'].get_process_name()} - "
                     f"{format_number(fastest_api['api_throughput'])} calls/s")

        # Most stable event processing (lowest CV%)
        most_stable_events = min(runs_with_data, key=lambda x: x['events_cv'] or float('inf'))
        report.append(f"- **Most Stable Event Processing:** {most_stable_events['test_run'].get_process_name()} - "
                     f"CV: {format_number(most_stable_events['events_cv'], 1)}%")

        # Most stable API (lowest CV%)
        most_stable_api = min(runs_with_data, key=lambda x: x['api_cv'] or float('inf'))
        report.append(f"- **Most Stable API Throughput:** {most_stable_api['test_run'].get_process_name()} - "
                     f"CV: {format_number(most_stable_api['api_cv'], 1)}%")

        # Lowest CPU usage
        lowest_cpu = min(runs_with_data, key=lambda x: x['avg_cpu'] or float('inf'))
        report.append(f"- **Lowest Average CPU Usage:** {lowest_cpu['test_run'].get_process_name()} - "
                     f"{format_number(lowest_cpu['avg_cpu'])}%")

        # Lowest memory usage
        lowest_memory = min(runs_with_data, key=lambda x: x['peak_memory'] or float('inf'))
        report.append(f"- **Lowest Peak Memory Usage:** {lowest_memory['test_run'].get_process_name()} - "
                     f"{format_number(lowest_memory['peak_memory'])} MB")

        report.append("")

    return '\n'.join(report)


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description='Compare performance test results and generate a markdown report.',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Compare all results in default folder (saves to test-results/test-report-comparison-{timestamp}.md)
  python compare_test_results.py

  # Compare results in specific folder
  python compare_test_results.py --folder /path/to/results

  # Print report to stdout instead of file
  python compare_test_results.py --stdout

  # Save report to custom location
  python compare_test_results.py --output /path/to/custom_report.md
        """
    )

    parser.add_argument(
        '--folder',
        type=str,
        default='./test-results',
        help='Folder containing test result JSON files (default: ./test-results)'
    )

    parser.add_argument(
        '--output',
        type=str,
        help='Output file for the report (default: test-report-comparison-{timestamp}.md in results folder)'
    )

    parser.add_argument(
        '--stdout',
        action='store_true',
        help='Print report to stdout instead of saving to file'
    )

    args = parser.parse_args()

    # Convert folder path to Path object
    folder_path = Path(args.folder)

    # Check if folder exists
    if not folder_path.exists():
        print(f"Error: Folder '{folder_path}' does not exist.", file=sys.stderr)
        sys.exit(1)

    if not folder_path.is_dir():
        print(f"Error: '{folder_path}' is not a directory.", file=sys.stderr)
        sys.exit(1)

    # Discover test runs
    print(f"Scanning folder: {folder_path}", file=sys.stderr)
    test_runs = discover_test_runs(folder_path)

    if not test_runs:
        print("No test results found in the specified folder.", file=sys.stderr)
        sys.exit(1)

    print(f"Found {len(test_runs)} test run(s)", file=sys.stderr)

    # Generate report
    report = generate_comparison_report(test_runs)

    # Output report
    if args.stdout:
        # Print to stdout
        print(report)
    elif args.output:
        # Use custom output path
        output_path = Path(args.output)
        with open(output_path, 'w') as f:
            f.write(report)
        print(f"Report saved to: {output_path}", file=sys.stderr)
    else:
        # Default: save to test-results folder with timestamp
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        output_filename = f"test-report-comparison-{timestamp}.md"
        output_path = folder_path / output_filename
        with open(output_path, 'w') as f:
            f.write(report)
        print(f"Report saved to: {output_path}", file=sys.stderr)


if __name__ == '__main__':
    main()
