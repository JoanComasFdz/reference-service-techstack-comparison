#!/usr/bin/env python3
"""
k6 load testing utilities.

Runs k6 load tests and parses output for metrics.
"""

import os
import re
import time
import subprocess
from datetime import datetime


def parse_duration_to_seconds(duration_str: str) -> int:
    """Parse duration string (k6 format) to seconds

    Args:
        duration_str: Duration in format: 10s, 5m, 2h

    Returns:
        Duration in seconds (default 30 if parsing fails)
    """
    duration_str = duration_str.lower()
    if duration_str.endswith('s'):
        return int(duration_str[:-1])
    elif duration_str.endswith('m'):
        return int(duration_str[:-1]) * 60
    elif duration_str.endswith('h'):
        return int(duration_str[:-1]) * 3600
    else:
        return 30  # Default fallback


def parse_k6_output(full_output: str, start_time: float, timeout_seconds: int,
                    api_throughput_samples: list[dict]) -> tuple[int, int, int]:
    """Parse k6 output to extract metrics and generate throughput samples

    Args:
        full_output: Full k6 output text
        start_time: Test start time (unix timestamp)
        timeout_seconds: Test timeout in seconds
        api_throughput_samples: List to append throughput samples to (modified in place)

    Returns:
        Tuple of (total_requests, success_count, error_count)
    """
    # FIRST: Parse final summary for accurate totals
    total_requests = 0
    success_count = 0
    error_count = 0

    # Look for "http_reqs......................: 3613   722.461133/s"
    match = re.search(r'http_reqs[.\s]*:\s*(\d+)', full_output)
    if match:
        total_requests = int(match.group(1))

    # Look for check success rate
    # "checks_succeeded...: 100.00% 2483 out of 2483"
    match = re.search(r'checks_succeeded[.\s]*:\s*([\d.]+)%\s*(\d+)\s*out of\s*(\d+)', full_output)
    if match:
        success_count = int(match.group(2))
        total_checks = int(match.group(3))
        error_count = total_checks - success_count
    else:
        # Assume all successful if no error info
        success_count = total_requests
        error_count = 0

    # SECOND: Parse progress lines to extract iteration counts over time
    # "running (01.0s), 1/1 VUs, 754 complete and 0 interrupted iterations"
    progress_pattern = r'running \((\d+\.\d+)s\).*?(\d+) complete.*?(\d+) interrupted'

    progress_data = []
    for line in full_output.split('\n'):
        match = re.search(progress_pattern, line)
        if match:
            elapsed_sec = float(match.group(1))
            complete_iterations = int(match.group(2))
            progress_data.append((elapsed_sec, complete_iterations))

    print(f"Found {len(progress_data)} progress samples from k6 output")

    # THIRD: If no progress data captured, generate interpolated samples
    if not progress_data and total_requests > 0:
        print("No progress data captured (k6 disables progress when output redirected)")
        print("Generating interpolated samples from total...")
        # Generate samples at 1-second intervals
        num_seconds = int(timeout_seconds)
        for sec in range(1, num_seconds + 1):
            # Estimate requests at this second (linear interpolation)
            est_requests = int((total_requests * sec) / timeout_seconds)
            progress_data.append((float(sec), est_requests))

    # FOURTH: Generate throughput samples from progress data
    if progress_data:
        for i, (elapsed_sec, iterations) in enumerate(progress_data):
            if i == 0:
                # First sample
                calls_per_sec = iterations / elapsed_sec if elapsed_sec > 0 else 0
            else:
                # Calculate throughput since last sample
                prev_elapsed, prev_iterations = progress_data[i - 1]
                time_diff = elapsed_sec - prev_elapsed
                iter_diff = iterations - prev_iterations
                calls_per_sec = iter_diff / time_diff if time_diff > 0 else 0

            api_throughput_samples.append({
                "timestamp": datetime.fromtimestamp(start_time + elapsed_sec).isoformat(),
                "elapsed_seconds": round(elapsed_sec, 3),
                "total_calls": iterations,
                "calls_per_second": round(calls_per_sec, 2)
            })

    return (total_requests, success_count, error_count)


def run_k6_test(
    script_path: str,
    api_url: str,
    duration: str,
    virtual_users: int,
    api_throughput_samples: list[dict]
) -> tuple[float, int, int]:
    """Run k6 load test and parse results

    Args:
        script_path: Path to k6 JavaScript test script
        api_url: API endpoint URL
        duration: Test duration string (e.g., "30s", "5m")
        virtual_users: Number of concurrent virtual users
        api_throughput_samples: List to append throughput samples to (modified in place)

    Returns:
        Tuple of (duration_sec, success_count, error_count)
    """
    # Check if k6 is available
    try:
        result = subprocess.run(['which', 'k6'], capture_output=True, text=True, timeout=2)
        if result.returncode != 0:
            print("ERROR: k6 is not installed")
            print("Install it with: sudo snap install k6")
            print("Or visit: https://k6.io/docs/get-started/installation/")
            return (0, 0, 0)
    except Exception as e:
        print(f"ERROR: Could not check for k6: {e}")
        return (0, 0, 0)

    # Parse duration to get timeout
    timeout_seconds = parse_duration_to_seconds(duration)

    # Build k6 command
    k6_cmd = [
        'k6', 'run',
        '--vus', str(virtual_users),
        '--duration', duration,
        '--env', f'API_URL={api_url}',
        script_path
    ]

    print(f"Starting k6 with {virtual_users} virtual users for {duration}...")
    print(f"Command: {' '.join(k6_cmd)}")

    start_time = time.time()

    # Create temporary file for k6 output
    script_dir = os.path.dirname(script_path)
    output_file = os.path.join(script_dir, f'k6-output-{int(start_time)}.txt')

    # Run k6 and use tee to show progress while saving output
    try:
        # Use tee to display output in real-time and save to file
        k6_cmd_str = ' '.join([f'"{arg}"' if ' ' in arg else arg for arg in k6_cmd])
        full_cmd = f'{k6_cmd_str} 2>&1 | tee "{output_file}"'

        result = subprocess.run(
            full_cmd,
            shell=True,
            timeout=timeout_seconds + 30
        )
    except subprocess.TimeoutExpired:
        print("ERROR: k6 timed out")
        try:
            os.remove(output_file)
        except (OSError, FileNotFoundError):
            pass
        return (0, 0, 0)
    except Exception as e:
        print(f"ERROR: k6 failed: {e}")
        try:
            os.remove(output_file)
        except (OSError, FileNotFoundError):
            pass
        return (0, 0, 0)

    elapsed = time.time() - start_time

    # Read full output from file
    try:
        with open(output_file, 'r') as f:
            full_output = f.read().replace('\r', '\n')
    except Exception as e:
        print(f"ERROR reading k6 output: {e}")
        return (0, 0, 0)
    finally:
        # Clean up output file
        try:
            os.remove(output_file)
        except (OSError, FileNotFoundError):
            pass

    print("k6 completed, parsing output...")

    # Parse k6 output to extract metrics
    total_requests, success_count, error_count = parse_k6_output(
        full_output, start_time, timeout_seconds, api_throughput_samples
    )

    # Calculate average throughput
    if elapsed > 0:
        avg_rps = total_requests / elapsed
    else:
        avg_rps = 0

    print(f"k6 completed: {total_requests} requests in {elapsed:.3f}s ({avg_rps:.2f} req/s)")
    print(f"Results: {success_count} success, {error_count} errors")

    return (elapsed, success_count, error_count)
