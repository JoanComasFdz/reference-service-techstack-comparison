#!/usr/bin/env python3
"""
Report generation utilities.

Generates JSON reports with throughput statistics and summaries.
"""

import json
import statistics
from datetime import datetime
from typing import Any
from collections import Counter


# Constants
SAMPLING_INTERVAL_MS = 100


def calculate_mode(values: list[float]) -> int:
    """Calculate mode (most common value) from a list of floats, rounded to nearest integer"""
    if not values:
        return 0
    rounded_values = [round(v) for v in values]
    return Counter(rounded_values).most_common(1)[0][0]


def calculate_std_dev(values: list[float]) -> float:
    """Calculate standard deviation from a list of values using statistics.stdev"""
    if not values or len(values) < 2:
        return 0.0
    return statistics.stdev(values)


def calculate_cv(values: list[float]) -> float:
    """Calculate coefficient of variation (CV) as a percentage

    CV = (standard deviation / mean) * 100

    Interpretation:
    - CV < 10%: Very stable/consistent performance
    - CV 10-20%: Moderate variability
    - CV > 20%: High variability/bursty behavior
    """
    if not values or len(values) < 2:
        return 0.0
    mean = statistics.mean(values)
    if mean == 0:
        return 0.0
    std_dev = statistics.stdev(values)
    return (std_dev / mean) * 100


def calculate_throughput_summary(samples: list[dict], rate_key: str, total_key: str, metric_name: str) -> dict[str, Any]:
    """Calculate throughput summary statistics

    Args:
        samples: List of sample dictionaries
        rate_key: Key for rate value in samples (e.g., 'events_per_second', 'calls_per_second')
        total_key: Key for cumulative total in samples (e.g., 'total_events', 'total_calls')
        metric_name: Name for metric fields (e.g., 'events', 'calls')

    Returns:
        Dictionary with summary statistics
    """
    if not samples:
        return {}

    rate_values = [s[rate_key] for s in samples]

    # Filter out zero values for min calculation (these are typically post-test measurements)
    # but keep them for average calculation to represent actual test conditions
    non_zero_rates = [r for r in rate_values if r > 0]

    avg_rate = sum(rate_values) / len(rate_values)

    return {
        f"avg_{metric_name}_per_second": round(avg_rate, 2),
        f"peak_{metric_name}_per_second": round(max(rate_values), 2),
        f"min_{metric_name}_per_second": round(min(non_zero_rates), 2) if non_zero_rates else 0,
        f"std_dev_{metric_name}_per_second": round(calculate_std_dev(rate_values), 2),
        f"cv_{metric_name}_per_second": round(calculate_cv(rate_values), 2),
        "avg_response_time_ms": round(1000.0 / avg_rate, 3) if avg_rate > 0 else 0,
        "total_samples": len(samples),
        f"total_{metric_name}": samples[-1][total_key] if samples else 0
    }


def save_throughput_report(filename: str, samples: list[dict], metric_name: str = "events") -> bool:
    """Save throughput metrics to JSON file

    Args:
        filename: Full path to output file
        samples: List of throughput sample dictionaries
        metric_name: Type of metric ('events' or 'calls')

    Returns:
        True if saved successfully, False otherwise
    """
    if not samples:
        print(f"No {metric_name} throughput samples collected")
        return False

    # Determine keys based on metric type
    if metric_name == "events":
        rate_key = "events_per_second"
        total_key = "total_events"
    else:  # calls
        rate_key = "calls_per_second"
        total_key = "total_calls"

    # Calculate summary statistics
    summary = calculate_throughput_summary(samples, rate_key, total_key, metric_name)

    report_data = {
        "test_date": datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
        "sampling_interval_ms": SAMPLING_INTERVAL_MS,
        "samples": samples,
        "summary": summary
    }

    with open(filename, 'w') as f:
        json.dump(report_data, f, indent=2)

    print(f"Throughput metrics saved to: {filename}")
    print(f"  Avg: {summary[f'avg_{metric_name}_per_second']} {metric_name}/s ({summary['avg_response_time_ms']:.3f}ms per {metric_name[:-1]})")
    print(f"  Peak: {summary[f'peak_{metric_name}_per_second']} {metric_name}/s")
    print(f"  Min: {summary[f'min_{metric_name}_per_second']} {metric_name}/s")
    print(f"  Std Dev: {summary[f'std_dev_{metric_name}_per_second']} {metric_name}/s")
    print(f"  CV: {summary[f'cv_{metric_name}_per_second']}% (variability)")

    return True


def write_test_report(
    filename: str,
    publish_time: float,
    consume_time: float,
    api_time: float,
    total_time: float,
    api_success: int,
    api_errors: int,
    system_info: dict[str, Any],
    num_events: int,
    api_duration: str,
    api_concurrent_workers: int,
    rabbitmq_exchange: str,
    consumer_queue: str,
    api_endpoint: str,
    publish_event_type: str,
    consume_event_type: str,
    monitor_info: dict[str, Any] | None = None,
    phase_timestamps: dict | None = None
) -> str:
    """Write comprehensive test report to file

    Args:
        filename: Full path to output file
        publish_time: Time spent publishing events (seconds)
        consume_time: Time spent consuming events (seconds)
        api_time: Time spent on API testing (seconds)
        total_time: Total test runtime (seconds)
        api_success: Number of successful API calls
        api_errors: Number of failed API calls
        system_info: Dictionary with system information
        num_events: Number of events published/consumed
        api_duration: API test duration string
        api_concurrent_workers: Number of concurrent API workers
        rabbitmq_exchange: RabbitMQ exchange name
        consumer_queue: Consumer queue name
        api_endpoint: API endpoint URL
        publish_event_type: Event type for publishing
        consume_event_type: Event type for consuming
        monitor_info: Optional dictionary with monitored process info
        phase_timestamps: Optional dictionary with phase timing information

    Returns:
        Path to report file
    """
    report_data = {
        "test_date": datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
        "total_runtime_seconds": round(total_time, 3),
        "system": system_info,
    }

    # Add phase timestamps if available
    if phase_timestamps is not None:
        report_data["phase_timestamps"] = {
            k: (round(v, 3) if v is not None else None)
            for k, v in phase_timestamps.items()
        }

    # Add monitored process info if available
    if monitor_info is not None:
        report_data["monitored_process"] = monitor_info

    report_data["configuration"] = {
        "num_events": num_events,
        "api_duration": api_duration,
        "api_concurrent_workers": api_concurrent_workers,
        "rabbitmq_exchange": rabbitmq_exchange,
        "consumer_queue": consumer_queue,
        "api_endpoint": api_endpoint,
        "publish_event_type": publish_event_type,
        "consume_event_type": consume_event_type
    }

    report_data["results"] = {
        "phase1_publish": {
            "duration_seconds": round(publish_time, 3),
            "throughput_events_per_sec": round(num_events/publish_time, 2) if publish_time > 0 else 0
        },
        "phase2_consume": {
            "duration_seconds": round(consume_time, 3),
            "throughput_events_per_sec": round(num_events/consume_time, 2) if consume_time > 0 else 0
        },
        "phase3_api": {
            "duration_seconds": round(api_time, 3),
            "total_requests": api_success + api_errors,
            "throughput_calls_per_sec": round((api_success + api_errors)/api_time, 2) if api_time > 0 else 0,
            "success_count": api_success,
            "success_percentage": round(api_success*100/(api_success + api_errors), 1) if (api_success + api_errors) > 0 else 0,
            "error_count": api_errors,
            "error_percentage": round(api_errors*100/(api_success + api_errors), 1) if (api_success + api_errors) > 0 else 0
        }
    }

    with open(filename, 'w') as f:
        json.dump(report_data, f, indent=2)

    print(f"\nReport written to: {filename}")
    return filename
