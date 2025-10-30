#!/usr/bin/env python3
"""
Chart generation utilities.

Generates performance visualization charts combining throughput, CPU, and RAM metrics.
"""

import os
import json
import matplotlib
matplotlib.use('Agg')  # Use non-interactive backend
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
from datetime import datetime, timedelta
from report_generator import calculate_mode, calculate_std_dev, calculate_cv


def format_throughput_legend(prefix: str, avg: float, response_time_ms: float,
                             min_val: float, max_val: float, mode_val: int,
                             std_dev: float, cv: float) -> str:
    """Format legend label for throughput metrics (events/API)

    Returns multiline label with statistics
    """
    return (f'{prefix} Avg: {avg:.1f} ({response_time_ms:.2f}ms)\n'
            f'Min: {min_val:.1f}\nMax: {max_val:.1f}\nMode: {mode_val}\n'
            f'Std Dev: {std_dev:.1f}\nCV: {cv:.1f}%')


def format_resource_legend(avg: float, min_val: float, max_val: float,
                           mode_val: int, unit: str) -> str:
    """Format legend label for resource metrics (CPU/RAM)

    Returns multiline label with statistics
    """
    return (f'Avg: {avg:.1f} {unit}\nMin: {min_val:.1f} {unit}\n'
            f'Max: {max_val:.1f} {unit}\nMode: {mode_val} {unit}')


def generate_metrics_chart(
    output_filename: str,
    throughput_file: str | None = None,
    api_throughput_file: str | None = None,
    resource_file: str | None = None,
    report_file: str | None = None,
    test_timestamp: str | None = None,
    rabbitmq_file: str | None = None,
    postgres_file: str | None = None,
    system_file: str | None = None
) -> bool:
    """Generate a combined chart showing throughput, CPU, and RAM metrics over time

    Args:
        output_filename: Full path to output PNG file
        throughput_file: Path to events throughput JSON file
        api_throughput_file: Path to API throughput JSON file
        resource_file: Path to resource metrics JSON file
        report_file: Path to main report JSON file
        test_timestamp: Test timestamp string (YYYYMMDD_HHMMSS) for chart title
        rabbitmq_file: Path to RabbitMQ container metrics JSON file
        postgres_file: Path to PostgreSQL container metrics JSON file
        system_file: Path to overall system metrics JSON file

    Returns:
        True if chart generated successfully, False otherwise
    """
    # Check if any data files exist
    if not throughput_file and not resource_file and not api_throughput_file:
        print("No metrics data available for chart generation")
        return False

    files_exist = (
        (throughput_file and os.path.exists(throughput_file)) or
        (resource_file and os.path.exists(resource_file)) or
        (api_throughput_file and os.path.exists(api_throughput_file))
    )

    if not files_exist:
        print("No metrics data files found for chart generation")
        return False

    # Humanize timestamp for title
    if test_timestamp:
        dt = datetime.strptime(test_timestamp, '%Y%m%d_%H%M%S')
        human_date = dt.strftime('%B %d, %Y at %H:%M:%S')
    else:
        human_date = datetime.now().strftime('%B %d, %Y at %H:%M:%S')

    # Get process name and test configuration from main report
    process_name = "Unknown Process"
    test_start_time = None
    num_events = None
    api_duration = None
    worker_count = None

    if report_file and os.path.exists(report_file):
        with open(report_file, 'r') as f:
            report_data = json.load(f)
            if report_data.get('monitored_process'):
                process_name = report_data['monitored_process'].get('name', 'Unknown Process')
            if report_data.get('configuration'):
                num_events = report_data['configuration'].get('num_events')
                api_duration = report_data['configuration'].get('api_duration')
                worker_count = report_data['configuration'].get('api_concurrent_workers')
            # Get test start time from test_date
            if report_data.get('test_date'):
                try:
                    test_start_time = datetime.strptime(report_data['test_date'], '%Y-%m-%d %H:%M:%S')
                except:
                    pass

    # Fallback to resource file for process name and start time if not in main report
    if process_name == "Unknown Process" and resource_file and os.path.exists(resource_file):
        with open(resource_file, 'r') as f:
            resource_data = json.load(f)
            if resource_data.get('process_info'):
                process_name = resource_data['process_info'].get('name', 'Unknown Process')
            if test_start_time is None and resource_data.get('test_date'):
                try:
                    test_start_time = datetime.strptime(resource_data['test_date'], '%Y-%m-%d %H:%M:%S')
                except:
                    pass

    # If we still don't have a start time, try to get it from the first sample in any file
    if test_start_time is None and throughput_file and os.path.exists(throughput_file):
        with open(throughput_file, 'r') as f:
            data = json.load(f)
            if data.get('samples') and len(data['samples']) > 0:
                try:
                    first_timestamp = datetime.fromisoformat(data['samples'][0]['timestamp'])
                    # Subtract elapsed_seconds to get the actual start time
                    test_start_time = first_timestamp - timedelta(seconds=data['samples'][0]['elapsed_seconds'])
                except:
                    pass

    # Create figure with 5 subplots (Throughput, Service CPU+RAM, RabbitMQ, PostgreSQL, Overall System)
    fig, (ax1, ax2, ax3, ax4, ax5) = plt.subplots(5, 1, figsize=(14, 16), sharex=True)
    fig.suptitle(f'Performance Metrics - {process_name} - {human_date}', fontsize=16, fontweight='bold')

    has_data = False

    # Plot combined throughput data (Events and API on same chart)
    ax1.set_ylabel('Throughput (per second)', fontsize=11, fontweight='bold')
    ax1.grid(True, alpha=0.3, axis='y')

    # Store phase boundaries for later
    phase1_start_time = None
    phase2_end_time = None
    phase3_start_time = None
    phase3_end_time = None

    # Plot events throughput data
    if throughput_file and os.path.exists(throughput_file):
        with open(throughput_file, 'r') as f:
            throughput_data = json.load(f)

        if throughput_data.get('samples'):
            # Use timestamps directly from the samples
            timestamps = [datetime.fromisoformat(s['timestamp']) for s in throughput_data['samples']]
            events_per_sec = [s['events_per_second'] for s in throughput_data['samples']]

            # Store phase boundaries
            phase1_start_time = timestamps[0]
            phase2_end_time = timestamps[-1]

            # Calculate statistics
            avg = throughput_data['summary']['avg_events_per_second']
            min_val = throughput_data['summary']['min_events_per_second']
            max_val = throughput_data['summary']['peak_events_per_second']
            std_dev = throughput_data['summary'].get('std_dev_events_per_second', calculate_std_dev(events_per_sec))
            cv = throughput_data['summary'].get('cv_events_per_second', calculate_cv(events_per_sec))
            response_time_ms = throughput_data['summary'].get('avg_response_time_ms', 1000.0 / avg if avg > 0 else 0)

            # Find mode (most common value, rounded to nearest integer)
            mode_val = calculate_mode(events_per_sec)

            ax1.plot(timestamps, events_per_sec, color='#2ecc71', linewidth=2, label='Consumed Events/sec')
            ax1.fill_between(timestamps, events_per_sec, alpha=0.3, color='#2ecc71')

            # Add average line with extended stats in legend (multiline)
            legend_label = format_throughput_legend('Events', avg, response_time_ms,
                                                   min_val, max_val, mode_val, std_dev, cv)
            ax1.axhline(y=avg, color='#27ae60', linestyle='--', alpha=0.7, label=legend_label)
            has_data = True

    # Plot API throughput data on same chart
    if api_throughput_file and os.path.exists(api_throughput_file):
        with open(api_throughput_file, 'r') as f:
            api_throughput_data = json.load(f)

        if api_throughput_data.get('samples'):
            # Use timestamps directly from the samples
            timestamps = [datetime.fromisoformat(s['timestamp']) for s in api_throughput_data['samples']]
            calls_per_sec = [s['calls_per_second'] for s in api_throughput_data['samples']]

            # Store phase boundaries
            phase3_start_time = timestamps[0]
            phase3_end_time = timestamps[-1]

            # Calculate statistics
            avg = api_throughput_data['summary']['avg_calls_per_second']
            min_val = api_throughput_data['summary']['min_calls_per_second']
            max_val = api_throughput_data['summary']['peak_calls_per_second']
            std_dev = api_throughput_data['summary'].get('std_dev_calls_per_second', calculate_std_dev(calls_per_sec))
            cv = api_throughput_data['summary'].get('cv_calls_per_second', calculate_cv(calls_per_sec))
            response_time_ms = api_throughput_data['summary'].get('avg_response_time_ms', 1000.0 / avg if avg > 0 else 0)

            # Find mode (most common value, rounded to nearest integer)
            mode_val = calculate_mode(calls_per_sec)

            ax1.plot(timestamps, calls_per_sec, color='#e67e22', linewidth=2, label='API calls/sec')
            ax1.fill_between(timestamps, calls_per_sec, alpha=0.3, color='#e67e22')

            # Add average line with extended stats in legend (multiline)
            legend_label = format_throughput_legend('API', avg, response_time_ms,
                                                   min_val, max_val, mode_val, std_dev, cv)
            ax1.axhline(y=avg, color='#d35400', linestyle='--', alpha=0.7, label=legend_label)
            has_data = True

    # Set y-axis limits to add 10% headroom at the top for labels
    if has_data:
        y_min, y_max = ax1.get_ylim()
        ax1.set_ylim(y_min, y_max * 1.1)
        ax1.legend(loc='upper left', bbox_to_anchor=(1.01, 1), borderaxespad=0, fontsize=9)

    # Plot Service CPU and RAM data (combined on dual y-axes)
    if resource_file and os.path.exists(resource_file):
        with open(resource_file, 'r') as f:
            resource_data = json.load(f)

        if resource_data.get('samples'):
            # Use timestamps directly
            timestamps = [datetime.fromisoformat(sample['timestamp']) for sample in resource_data['samples']]
            cpu_percent = [sample['cpu_percent'] for sample in resource_data['samples']]
            memory_rss = [sample['memory_rss_mb'] for sample in resource_data['samples']]

            # Calculate CPU statistics
            avg_cpu = resource_data['summary']['avg_cpu_percent']
            min_cpu = min(cpu_percent)
            max_cpu = resource_data['summary']['peak_cpu_percent']
            mode_cpu = calculate_mode(cpu_percent)

            # Calculate RAM statistics
            avg_ram = resource_data['summary']['avg_memory_rss_mb']
            min_ram = min(memory_rss)
            max_ram = resource_data['summary']['peak_memory_rss_mb']
            mode_ram = calculate_mode(memory_rss)

            # Plot CPU on primary axis
            ax2.plot(timestamps, cpu_percent, color='#3498db', linewidth=2, label='CPU %')
            ax2.fill_between(timestamps, cpu_percent, alpha=0.3, color='#3498db')
            ax2.set_ylabel('Service CPU (%)', fontsize=11, fontweight='bold', color='#3498db')
            ax2.tick_params(axis='y', labelcolor='#3498db')
            ax2.grid(True, alpha=0.3, axis='y')

            # Add average line with stats
            legend_label = format_resource_legend(avg_cpu, min_cpu, max_cpu, mode_cpu, '%')
            ax2.axhline(y=avg_cpu, color='#2980b9', linestyle='--', alpha=0.7, label=legend_label)

            # Create secondary y-axis for RAM
            ax2_mem = ax2.twinx()
            ax2_mem.plot(timestamps, memory_rss, color='#e74c3c', linewidth=2, label='RAM')
            ax2_mem.fill_between(timestamps, memory_rss, alpha=0.2, color='#e74c3c')
            ax2_mem.set_ylabel('Service RAM (MB)', fontsize=11, fontweight='bold', color='#e74c3c')
            ax2_mem.tick_params(axis='y', labelcolor='#e74c3c')

            # Add average line for RAM
            legend_label_mem = format_resource_legend(avg_ram, min_ram, max_ram, mode_ram, 'MB')
            ax2_mem.axhline(y=avg_ram, color='#c0392b', linestyle='--', alpha=0.7, label=legend_label_mem)

            # Combine legends
            lines1, labels1 = ax2.get_legend_handles_labels()
            lines2, labels2 = ax2_mem.get_legend_handles_labels()
            ax2.legend(lines1 + lines2, labels1 + labels2, loc='upper left', bbox_to_anchor=(1.15, 1), borderaxespad=0, fontsize=9)

            has_data = True

    # Plot RabbitMQ metrics (CPU and RAM on dual y-axes)
    if rabbitmq_file and os.path.exists(rabbitmq_file):
        with open(rabbitmq_file, 'r') as f:
            rabbitmq_data = json.load(f)

        if rabbitmq_data.get('samples'):
            timestamps = [datetime.fromisoformat(sample['timestamp']) for sample in rabbitmq_data['samples']]
            cpu_percent = [sample['cpu_percent'] for sample in rabbitmq_data['samples']]
            memory_mb = [sample['memory_mb'] for sample in rabbitmq_data['samples']]

            # Calculate statistics
            avg_cpu = rabbitmq_data['summary']['avg_cpu_percent']
            min_cpu = min(cpu_percent)
            max_cpu = rabbitmq_data['summary']['peak_cpu_percent']
            mode_cpu = calculate_mode(cpu_percent)

            avg_mem = rabbitmq_data['summary']['avg_memory_mb']
            min_mem = min(memory_mb)
            max_mem = rabbitmq_data['summary']['peak_memory_mb']
            mode_mem = calculate_mode(memory_mb)

            # Plot CPU on primary axis
            ax3.plot(timestamps, cpu_percent, color='#9b59b6', linewidth=2, label='CPU %')
            ax3.fill_between(timestamps, cpu_percent, alpha=0.3, color='#9b59b6')
            ax3.set_ylabel('RabbitMQ CPU (%)', fontsize=11, fontweight='bold', color='#9b59b6')
            ax3.tick_params(axis='y', labelcolor='#9b59b6')
            ax3.grid(True, alpha=0.3, axis='y')

            # Add average line with stats
            legend_label = format_resource_legend(avg_cpu, min_cpu, max_cpu, mode_cpu, '%')
            ax3.axhline(y=avg_cpu, color='#8e44ad', linestyle='--', alpha=0.7, label=legend_label)

            # Create secondary y-axis for RAM
            ax3_mem = ax3.twinx()
            ax3_mem.plot(timestamps, memory_mb, color='#e67e22', linewidth=2, label='RAM')
            ax3_mem.fill_between(timestamps, memory_mb, alpha=0.2, color='#e67e22')
            ax3_mem.set_ylabel('RabbitMQ RAM (MB)', fontsize=11, fontweight='bold', color='#e67e22')
            ax3_mem.tick_params(axis='y', labelcolor='#e67e22')

            # Add average line for RAM
            legend_label_mem = format_resource_legend(avg_mem, min_mem, max_mem, mode_mem, 'MB')
            ax3_mem.axhline(y=avg_mem, color='#d35400', linestyle='--', alpha=0.7, label=legend_label_mem)

            # Combine legends
            lines1, labels1 = ax3.get_legend_handles_labels()
            lines2, labels2 = ax3_mem.get_legend_handles_labels()
            ax3.legend(lines1 + lines2, labels1 + labels2, loc='upper left', bbox_to_anchor=(1.15, 1), borderaxespad=0, fontsize=9)

            has_data = True

    # Plot PostgreSQL metrics (CPU and RAM on dual y-axes)
    if postgres_file and os.path.exists(postgres_file):
        with open(postgres_file, 'r') as f:
            postgres_data = json.load(f)

        if postgres_data.get('samples'):
            timestamps = [datetime.fromisoformat(sample['timestamp']) for sample in postgres_data['samples']]
            cpu_percent = [sample['cpu_percent'] for sample in postgres_data['samples']]
            memory_mb = [sample['memory_mb'] for sample in postgres_data['samples']]

            # Calculate statistics
            avg_cpu = postgres_data['summary']['avg_cpu_percent']
            min_cpu = min(cpu_percent)
            max_cpu = postgres_data['summary']['peak_cpu_percent']
            mode_cpu = calculate_mode(cpu_percent)

            avg_mem = postgres_data['summary']['avg_memory_mb']
            min_mem = min(memory_mb)
            max_mem = postgres_data['summary']['peak_memory_mb']
            mode_mem = calculate_mode(memory_mb)

            # Plot CPU on primary axis
            ax4.plot(timestamps, cpu_percent, color='#16a085', linewidth=2, label='CPU %')
            ax4.fill_between(timestamps, cpu_percent, alpha=0.3, color='#16a085')
            ax4.set_ylabel('PostgreSQL CPU (%)', fontsize=11, fontweight='bold', color='#16a085')
            ax4.tick_params(axis='y', labelcolor='#16a085')
            ax4.grid(True, alpha=0.3, axis='y')

            # Add average line with stats
            legend_label = format_resource_legend(avg_cpu, min_cpu, max_cpu, mode_cpu, '%')
            ax4.axhline(y=avg_cpu, color='#138d75', linestyle='--', alpha=0.7, label=legend_label)

            # Create secondary y-axis for RAM
            ax4_mem = ax4.twinx()
            ax4_mem.plot(timestamps, memory_mb, color='#f39c12', linewidth=2, label='RAM')
            ax4_mem.fill_between(timestamps, memory_mb, alpha=0.2, color='#f39c12')
            ax4_mem.set_ylabel('PostgreSQL RAM (MB)', fontsize=11, fontweight='bold', color='#f39c12')
            ax4_mem.tick_params(axis='y', labelcolor='#f39c12')

            # Add average line for RAM
            legend_label_mem = format_resource_legend(avg_mem, min_mem, max_mem, mode_mem, 'MB')
            ax4_mem.axhline(y=avg_mem, color='#e67e22', linestyle='--', alpha=0.7, label=legend_label_mem)

            # Combine legends
            lines1, labels1 = ax4.get_legend_handles_labels()
            lines2, labels2 = ax4_mem.get_legend_handles_labels()
            ax4.legend(lines1 + lines2, labels1 + labels2, loc='upper left', bbox_to_anchor=(1.15, 1), borderaxespad=0, fontsize=9)

            has_data = True

    # Plot Overall System metrics (CPU and RAM on dual y-axes)
    if system_file and os.path.exists(system_file):
        with open(system_file, 'r') as f:
            system_data = json.load(f)

        if system_data.get('samples'):
            timestamps = [datetime.fromisoformat(sample['timestamp']) for sample in system_data['samples']]
            cpu_percent = [sample['cpu_percent'] for sample in system_data['samples']]
            memory_used_mb = [sample['memory_used_mb'] for sample in system_data['samples']]

            # Get total system memory and CPU count from the data
            # Use Windows host RAM if available (WSL2), otherwise use WSL2 VM allocated RAM
            if system_data.get('windows_host_total_ram_mb'):
                total_memory_mb = system_data['windows_host_total_ram_mb']
            elif system_data['samples']:
                total_memory_mb = system_data['samples'][0]['memory_total_mb']
            else:
                total_memory_mb = 0

            cpu_count = system_data.get('cpu_count', 1)  # Default to 1 if not available

            # Calculate statistics
            avg_cpu = system_data['summary']['avg_cpu_percent']
            min_cpu = system_data['summary']['min_cpu_percent']
            max_cpu = system_data['summary']['peak_cpu_percent']
            mode_cpu = calculate_mode(cpu_percent)

            avg_mem = system_data['summary']['avg_memory_used_mb']
            min_mem = min(memory_used_mb)
            max_mem = system_data['summary']['peak_memory_used_mb']
            mode_mem = calculate_mode(memory_used_mb)

            # Plot CPU on primary axis
            ax5.plot(timestamps, cpu_percent, color='#34495e', linewidth=2, label='CPU %')
            ax5.fill_between(timestamps, cpu_percent, alpha=0.3, color='#34495e')
            ax5.set_ylabel('Overall System CPU (%)', fontsize=11, fontweight='bold', color='#34495e')
            ax5.tick_params(axis='y', labelcolor='#34495e')
            ax5.grid(True, alpha=0.3, axis='y')

            # Set CPU Y-axis to 0-100%
            ax5.set_ylim(0, 100)

            # Add average line with stats
            legend_label = format_resource_legend(avg_cpu, min_cpu, max_cpu, mode_cpu, '%')
            ax5.axhline(y=avg_cpu, color='#2c3e50', linestyle='--', alpha=0.7, label=legend_label)

            # Create secondary y-axis for RAM
            ax5_mem = ax5.twinx()
            ax5_mem.plot(timestamps, memory_used_mb, color='#c0392b', linewidth=2, label='RAM')
            ax5_mem.fill_between(timestamps, memory_used_mb, alpha=0.2, color='#c0392b')

            # Label indicates if using Windows host RAM (WSL2) or system RAM
            ram_label = 'Overall System RAM (MB)'
            if system_data.get('is_wsl2') and system_data.get('windows_host_total_ram_mb'):
                ram_label = 'Overall System RAM - Windows Host (MB)'

            ax5_mem.set_ylabel(ram_label, fontsize=11, fontweight='bold', color='#c0392b')
            ax5_mem.tick_params(axis='y', labelcolor='#c0392b')

            # Set RAM Y-axis to 0-total_memory to show full system capacity
            if total_memory_mb > 0:
                ax5_mem.set_ylim(0, total_memory_mb)

            # Add average line for RAM
            legend_label_mem = format_resource_legend(avg_mem, min_mem, max_mem, mode_mem, 'MB')
            ax5_mem.axhline(y=avg_mem, color='#a93226', linestyle='--', alpha=0.7, label=legend_label_mem)

            # Combine legends
            lines1, labels1 = ax5.get_legend_handles_labels()
            lines2, labels2 = ax5_mem.get_legend_handles_labels()
            ax5.legend(lines1 + lines2, labels1 + labels2, loc='upper left', bbox_to_anchor=(1.15, 1), borderaxespad=0, fontsize=9)

            has_data = True

    if not has_data:
        plt.close(fig)
        print("No valid metrics data for chart generation")
        return False

    # Format X-axis with timestamps (now using ax5 as the bottom subplot)
    ax5.set_xlabel('Time', fontsize=11, fontweight='bold')
    ax5.xaxis.set_major_formatter(mdates.DateFormatter('%H:%M:%S'))
    ax5.xaxis.set_major_locator(mdates.AutoDateLocator())
    plt.setp(ax5.xaxis.get_majorticklabels(), rotation=45, ha='right')

    # Draw phase boundary lines on all subplots
    phase_boundaries = [
        (phase1_start_time, '#27ae60'),
        (phase2_end_time, '#2980b9'),
        (phase3_start_time, '#e67e22'),
        (phase3_end_time, '#e67e22'),
    ]

    for phase_time, color in phase_boundaries:
        if phase_time:
            for ax in [ax1, ax2, ax3, ax4, ax5]:
                ax.axvline(x=phase_time, color=color, linestyle=':', linewidth=1.5, alpha=0.6)

    # Add phase labels to top subplot
    if phase1_start_time and phase2_end_time:
        mid_time = phase1_start_time + (phase2_end_time - phase1_start_time) / 2
        label = f'Consume: {num_events}' if num_events else 'Consume'
        ax1.text(mid_time, ax1.get_ylim()[1] * 0.95, label,
                ha='center', va='top', fontsize=9, color='#27ae60', fontweight='bold')

    if phase3_start_time and phase3_end_time:
        mid_time = phase3_start_time + (phase3_end_time - phase3_start_time) / 2
        if api_duration and worker_count:
            label = f'API: {api_duration} ({worker_count}w)'
        elif api_duration:
            label = f'API: {api_duration}'
        else:
            label = 'API'
        ax1.text(mid_time, ax1.get_ylim()[1] * 0.95, label,
                ha='center', va='top', fontsize=9, color='#e67e22', fontweight='bold')

    # Save the chart
    plt.tight_layout()
    plt.savefig(output_filename, dpi=150, bbox_inches='tight')
    plt.close(fig)

    print(f"Metrics chart saved to: {output_filename}")
    return True
