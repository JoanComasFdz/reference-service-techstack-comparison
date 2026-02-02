#!/usr/bin/env python3
"""
Service Performance Tester

Orchestrates comprehensive service testing by:
1. Publishing events to RabbitMQ
2. Consuming events from RabbitMQ
3. Calling the HTTP API (using k6 load testing tool)
4. Monitoring CPU and memory usage
5. Generating detailed reports with metrics and charts
"""

import pika
import time
import threading
import os
import re
import subprocess
import argparse
from dataclasses import dataclass, field
from datetime import datetime

# Import configuration and functions from send_events
from send_events import (
    RABBITMQ_HOST,
    RABBITMQ_PORT,
    RABBITMQ_USER,
    RABBITMQ_PASS,
    EXCHANGE_NAME,
    EVENT_TYPE as PUBLISH_EVENT_TYPE,
    send_multiple_events
)

# Import from our new modules
from system_info import get_system_info
from process_monitor import ProcessMonitor
from container_monitor import ContainerMonitor
from system_monitor import SystemMonitor
from report_generator import save_throughput_report, write_test_report
from chart_generator import generate_metrics_chart
from k6_runner import run_k6_test

# Additional configuration - read from environment variables with defaults
CONSUME_EVENT_TYPE = os.getenv('RABBITMQ_CONSUME_EVENT_TYPE', 'instrumentstatus.kpi.updated')
CONSUMER_QUEUE = os.getenv('RABBITMQ_CONSUMER_QUEUE', 'service-tester')

# API configuration (default port, can be overridden by CLI args)
API_PORT = 8080
API_URL = f'http://localhost:{API_PORT}/kpi'

# Test configuration (defaults, can be overridden by CLI args)
NUM_EVENTS = 10000  # Total number of events to publish and consume
API_DURATION = "30s"  # Duration for API testing (k6 format: 10s, 5m, 2h)
API_CONCURRENT_WORKERS = 1  # Number of concurrent virtual users for k6

# Warmup configuration (to achieve steady-state performance for JIT services)
WARMUP_EVENTS = 200  # Number of events for warmup phase
WARMUP_API_DURATION = "5s"  # Duration for API warmup (k6 format)

# Get the directory where this script is located
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RESULTS_FOLDER = os.path.join(SCRIPT_DIR, 'test-results')

# RabbitMQ and API testing constants - read from environment variables with defaults
RABBITMQ_PREFETCH_COUNT = int(os.getenv('RABBITMQ_PREFETCH_COUNT', '100'))
CONSUMER_SETUP_DELAY_SEC = 1
FINAL_MONITORING_DELAY_SEC = 1.0
DEFAULT_SERVICE_MONITORING_INTERVAL_MS = 500
CONTAINER_MONITORING_INTERVAL_MS = 3000  # Slower sampling for containers to reduce docker stats overhead

# Sampling interval for throughput tracking
SAMPLING_INTERVAL_MS = 100
SAMPLING_INTERVAL_SEC = SAMPLING_INTERVAL_MS / 1000.0


@dataclass
class TestState:
    """Encapsulates mutable state for test execution"""
    received_event_count: int = 0
    received_lock: threading.Lock = field(default_factory=threading.Lock)
    throughput_samples: list[dict] = field(default_factory=list)
    api_throughput_samples: list[dict] = field(default_factory=list)


def log(message):
    """Print log message"""
    print(message)


def sanitize_filename(name: str) -> str:
    """Sanitize a string for use in filenames"""
    if not name:
        return "unknown"
    # Remove file extensions
    name = re.sub(r'\.(jar|dll|exe)$', '', name)
    # Replace spaces and special chars with dashes
    name = re.sub(r'[^\w\-.]', '-', name)
    # Remove consecutive dashes
    name = re.sub(r'-+', '-', name)
    # Remove leading/trailing dashes
    name = name.strip('-')
    return name.lower() if name else "unknown"


def get_report_filename(test_timestamp: str, suffix: str, program_name: str = "") -> str:
    """Generate a consistent report filename with optional program name

    Args:
        test_timestamp: Timestamp string in format YYYYMMDD_HHMMSS
        suffix: File suffix (e.g., 'resource-metrics.json', 'chart.png')
        program_name: Optional program name to include in filename

    Returns:
        Full path to report file
    """
    if program_name:
        filename = f"test-report-{test_timestamp}-{program_name}.{suffix}"
    else:
        filename = f"test-report-{test_timestamp}.{suffix}"
    return os.path.join(RESULTS_FOLDER, filename)


def publish_events(channel) -> float:
    """Publish events to RabbitMQ as fast as possible

    Returns:
        Elapsed time in seconds
    """
    log(f"Starting to publish {NUM_EVENTS} events...")
    start_time = time.time()

    # Use send_multiple_events with delay=0 for maximum speed
    send_multiple_events(channel, NUM_EVENTS, delay=0, verbose=False)

    elapsed = time.time() - start_time
    log(f"Published {NUM_EVENTS} events in {elapsed:.3f} seconds ({NUM_EVENTS/elapsed:.2f} events/sec)")
    return elapsed


def consume_events(test_state: TestState) -> None:
    """Consume events from RabbitMQ in a separate thread"""
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
    parameters = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        port=RABBITMQ_PORT,
        credentials=credentials
    )

    connection = pika.BlockingConnection(parameters)
    channel = connection.channel()

    # Declare exchange
    channel.exchange_declare(
        exchange=EXCHANGE_NAME,
        exchange_type='topic',
        durable=True
    )

    # Declare queue
    channel.queue_declare(queue=CONSUMER_QUEUE, durable=True)

    # Bind queue to exchange
    channel.queue_bind(
        exchange=EXCHANGE_NAME,
        queue=CONSUMER_QUEUE,
        routing_key=CONSUME_EVENT_TYPE
    )

    log(f"Consumer ready, listening for {CONSUME_EVENT_TYPE} events...")

    # Track throughput - sample every 100ms to match resource monitoring
    start_time = time.time()
    last_sample_time = start_time
    last_sample_count = 0
    sampling_interval = SAMPLING_INTERVAL_SEC

    # Track console logging - log every second
    last_log_time = start_time
    last_log_count = 0
    log_interval = 1.0  # Log every second

    def callback(ch, method, properties, body):
        nonlocal last_sample_time, last_sample_count, last_log_time, last_log_count

        with test_state.received_lock:
            test_state.received_event_count += 1
            count = test_state.received_event_count

        current_time = time.time()

        # Sample throughput every 100ms for metrics
        if current_time - last_sample_time >= sampling_interval:
            events_in_period = count - last_sample_count
            elapsed_since_sample = current_time - last_sample_time
            events_per_sec = events_in_period / elapsed_since_sample

            test_state.throughput_samples.append({
                "timestamp": datetime.now().isoformat(),
                "elapsed_seconds": round(current_time - start_time, 3),
                "total_events": count,
                "events_per_second": round(events_per_sec, 2)
            })

            last_sample_time = current_time
            last_sample_count = count

        # Log progress every second
        if count == 1:
            log(f"Consuming: started receiving events...")
            last_log_time = current_time
            last_log_count = count
        elif current_time - last_log_time >= log_interval or count >= NUM_EVENTS:
            elapsed_total = current_time - start_time
            events_in_log_period = count - last_log_count
            time_in_log_period = current_time - last_log_time
            events_per_sec_current = events_in_log_period / time_in_log_period if time_in_log_period > 0 else 0

            log(f"Consuming ({elapsed_total:.1f}s): {count}/{NUM_EVENTS} events ({events_per_sec_current:.0f} events/sec)")

            last_log_time = current_time
            last_log_count = count

        ch.basic_ack(delivery_tag=method.delivery_tag)

        if count >= NUM_EVENTS:
            ch.stop_consuming()

    channel.basic_qos(prefetch_count=RABBITMQ_PREFETCH_COUNT)
    channel.basic_consume(queue=CONSUMER_QUEUE, on_message_callback=callback)

    try:
        channel.start_consuming()
    except Exception as e:
        log(f"Consumer stopped: {e}")
    finally:
        connection.close()


def wait_for_service_on_port(port: int, timeout_seconds: int = 30) -> bool:
    """Wait for a service to start on the specified port

    Returns:
        True if service was found, False if timeout expired
    """
    log(f"Checking for service on port {port}...")
    try:
        result = subprocess.run(['lsof', '-ti', f':{port}'],
                              capture_output=True, text=True, timeout=2)
        if result.returncode == 0 and result.stdout.strip():
            # Service already running
            pid = result.stdout.strip().split('\n')[0]
            log(f"✓ Service detected on port {port} (PID: {pid})")
            return True

        # No process found - wait for it to start
        log(f"⚠️  WARNING: No service detected on port {port}!")
        log(f"The service-tester requires a running service to monitor.")
        log(f"Please start your service on port {port}, or press Ctrl+C to cancel.")
        log(f"\nWaiting for service to start...")

        found = False
        for i in range(timeout_seconds):
            time.sleep(1)
            result = subprocess.run(['lsof', '-ti', f':{port}'],
                                  capture_output=True, text=True, timeout=2)
            if result.returncode == 0 and result.stdout.strip():
                found = True
                pid = result.stdout.strip().split('\n')[0]
                log(f"✓ Service detected on port {port} (PID: {pid})")
                break

            if (i + 1) % 5 == 0:
                log(f"Still waiting... ({i + 1}/{timeout_seconds}s)")

        if not found:
            log(f"\n❌ ERROR: No service started within {timeout_seconds} seconds.")
            log("Exiting. Please start your service and run the tester again.")
            return False
        return True

    except Exception as e:
        log(f"WARNING: Could not check for service on port {port}: {e}")
        log("Continuing anyway...")
        return True  # Continue despite error


def get_database_name_from_port(port: int) -> str:
    """Map service port to database name"""
    port_to_db = {
        8090: "bun_db",
        8092: "dotnet9_db",
        8093: "dotnet9aot_db",
        8094: "go_db",
        8096: "java21quarkus_db",
        8097: "java21springboot_db",
        8098: "java21springbootgraal_db",
        8099: "python_db",
        8100: "rust_db",
        8101: "java25springboot_db",
        8102: "java25springbootgraal_db",
        8103: "java25quarkus_db"
    }

    db_name = port_to_db.get(port)
    if db_name is None:
        raise ValueError(f"Unknown port {port} - cannot determine database name")
    return db_name


def clear_database(port: int, max_retries: int = 2) -> None:
    """Clear the database for a clean test run"""
    log("\nClearing database for clean test run...")

    try:
        db_name = get_database_name_from_port(port)
        log(f"Database: {db_name}")
    except ValueError as e:
        log(f"ERROR: {e}")
        raise

    for attempt in range(1, max_retries + 1):
        try:
            result = subprocess.run(['./clean-service-data.sh', db_name],
                                  capture_output=True, text=True, timeout=30)
            if result.returncode == 0:
                log("✓ Database cleared successfully")
                return
            else:
                log(f"WARNING: Database clear failed (attempt {attempt}/{max_retries})")
                log(f"stderr: {result.stderr}")
                if attempt < max_retries:
                    log(f"Retrying in 2 seconds...")
                    time.sleep(2)
        except subprocess.TimeoutExpired:
            log(f"WARNING: Database clear timed out (attempt {attempt}/{max_retries})")
            if attempt < max_retries:
                log(f"Retrying in 2 seconds...")
                time.sleep(2)
        except Exception as e:
            log(f"WARNING: Database clear error (attempt {attempt}/{max_retries}): {e}")
            if attempt < max_retries:
                log(f"Retrying in 2 seconds...")
                time.sleep(2)

    # If we got here, all attempts failed
    raise RuntimeError(f"Failed to clear database after {max_retries} attempts")


def clear_rabbitmq() -> None:
    """Clear RabbitMQ queues for a clean test run"""
    log("\nClearing RabbitMQ queues for clean test run...")
    try:
        result = subprocess.run(['./clear-rabbitmq.sh'],
                              capture_output=True, text=True, timeout=10)
        if result.returncode == 0:
            log("✓ RabbitMQ queues cleared successfully")
        else:
            log(f"WARNING: RabbitMQ clear failed: {result.stderr}")
            log("Continuing anyway...")
    except Exception as e:
        log(f"WARNING: Could not clear RabbitMQ queues: {e}")
        log("Continuing anyway...")


def main():
    # Parse command-line arguments
    parser = argparse.ArgumentParser(
        description='Test service performance by publishing/consuming events and calling the HTTP API',
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )
    parser.add_argument(
        '--events',
        type=int,
        default=10000,
        help='Number of events to publish and consume'
    )
    parser.add_argument(
        '--api-duration',
        type=str,
        default='30s',
        help='Duration for API load testing (format: 10s, 5m, 2h). k6 will push maximum load for this duration.'
    )
    parser.add_argument(
        '--api-workers',
        type=int,
        default=1,
        help='Number of concurrent virtual users for k6'
    )
    parser.add_argument(
        '--results-folder',
        type=str,
        default='./test-results',
        help='Folder to save test results and reports'
    )
    parser.add_argument(
        '--port',
        type=int,
        default=8080,
        help='Port where the service is running'
    )
    args = parser.parse_args()

    # Create test state to track execution
    test_state = TestState()

    # Update global configuration with CLI arguments
    global NUM_EVENTS, API_DURATION, API_CONCURRENT_WORKERS, RESULTS_FOLDER, API_PORT, API_URL
    NUM_EVENTS = args.events
    API_DURATION = args.api_duration
    API_CONCURRENT_WORKERS = args.api_workers
    RESULTS_FOLDER = args.results_folder
    API_PORT = args.port
    API_URL = f'http://localhost:{API_PORT}/kpi'

    # Create results folder if it doesn't exist
    os.makedirs(RESULTS_FOLDER, exist_ok=True)

    log("=" * 80)
    log("SERVICE TESTER STARTED")
    log("=" * 80)
    log(f"Configuration: {NUM_EVENTS} events, API test for {API_DURATION} ({API_CONCURRENT_WORKERS} virtual users)")
    log(f"Testing service on port {API_PORT}")

    # Wait for service to be ready
    if not wait_for_service_on_port(API_PORT):
        return

    # Clear database and RabbitMQ for clean test
    clear_database(API_PORT)
    clear_rabbitmq()

    # Start process monitoring
    log(f"\nStarting process monitoring on port {API_PORT}...")
    monitor = ProcessMonitor(port=API_PORT, sampling_interval_ms=DEFAULT_SERVICE_MONITORING_INTERVAL_MS)
    monitor.start_monitoring()

    # Start RabbitMQ container monitoring (slower sampling to reduce overhead)
    log("\nStarting RabbitMQ container monitoring...")
    rabbitmq_container_name = os.getenv('RABBITMQ_CONTAINER_NAME', 'performancetest-rabbitmq')
    rabbitmq_monitor = ContainerMonitor(container_name=rabbitmq_container_name,
                                       sampling_interval_ms=CONTAINER_MONITORING_INTERVAL_MS)
    rabbitmq_monitor.start_monitoring()

    # Start PostgreSQL container monitoring (slower sampling to reduce overhead)
    log("\nStarting PostgreSQL container monitoring...")
    postgres_container_name = os.getenv('POSTGRES_CONTAINER_NAME', 'performancetest-postgres')
    postgres_monitor = ContainerMonitor(container_name=postgres_container_name,
                                       sampling_interval_ms=CONTAINER_MONITORING_INTERVAL_MS)
    postgres_monitor.start_monitoring()

    # Start overall system monitoring
    log("\nStarting overall system monitoring...")
    system_wide_monitor = SystemMonitor(sampling_interval_ms=DEFAULT_SERVICE_MONITORING_INTERVAL_MS)
    system_wide_monitor.start_monitoring()


    total_start_time = time.time()

    # Initialize timing variables in case phases are skipped
    publish_time = 0.0
    consume_time = 0.0
    api_time = 0.0
    api_success = 0
    api_errors = 0
    consumer_thread = None

    # Track phase timestamps (relative to total_start_time)
    phase_timestamps: dict[str, float | None] = {
        "phase1_start": None,
        "phase1_end": None,
        "phase2_start": None,
        "phase2_end": None,
        "phase3_start": None,
        "phase3_end": None
    }

    # Phase 0: Warmup (for steady-state performance testing)
    log("\n" + "=" * 80)
    log("PHASE 0: WARMUP (not included in performance metrics)")
    log("=" * 80)
    log("Purpose: Warm up JIT compilers, caches, and connection pools for steady-state performance")
    log(f"Warmup: {WARMUP_EVENTS} events + {WARMUP_API_DURATION} API load test")

    try:
        # Setup warmup connection
        credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
        parameters = pika.ConnectionParameters(
            host=RABBITMQ_HOST,
            port=RABBITMQ_PORT,
            credentials=credentials
        )

        log(f"Connecting to RabbitMQ for warmup...")
        warmup_connection = pika.BlockingConnection(parameters)
        warmup_channel = warmup_connection.channel()

        # Declare exchange
        warmup_channel.exchange_declare(
            exchange=EXCHANGE_NAME,
            exchange_type='topic',
            durable=True
        )

        # Start warmup consumer
        log("Starting warmup consumer...")
        warmup_consumer_thread = threading.Thread(target=consume_events, args=(test_state,), daemon=True)
        warmup_consumer_thread.start()
        time.sleep(CONSUMER_SETUP_DELAY_SEC)

        # Publish warmup events
        log(f"Publishing {WARMUP_EVENTS} warmup events...")
        warmup_start = time.time()
        send_multiple_events(warmup_channel, WARMUP_EVENTS, delay=0, verbose=False)
        warmup_connection.close()

        # Wait for warmup events to be consumed
        log("Consuming warmup events...")
        warmup_timeout = 30
        warmup_elapsed = 0
        while warmup_elapsed < warmup_timeout:
            with test_state.received_lock:
                count = test_state.received_event_count
            if count >= WARMUP_EVENTS:
                break
            time.sleep(0.1)
            warmup_elapsed = time.time() - warmup_start

        log(f"✓ Warmup events processed ({WARMUP_EVENTS} events in {warmup_elapsed:.1f}s)")

        # Warmup API calls
        log(f"Warming up API with {WARMUP_API_DURATION} load test...")
        warmup_api_samples: list[dict] = []  # Discard warmup API samples
        k6_script_path = os.path.join(SCRIPT_DIR, 'api-load-test.js')
        warmup_api_time, warmup_success, warmup_errors = run_k6_test(
            k6_script_path,
            API_URL,
            WARMUP_API_DURATION,
            API_CONCURRENT_WORKERS,
            warmup_api_samples
        )
        log(f"✓ API warmup completed ({warmup_success + warmup_errors} requests in {warmup_api_time:.1f}s)")

        # Clean database after warmup
        log("Clearing database after warmup...")
        clear_database(API_PORT)
        log("✓ Database cleared")

        # Clean RabbitMQ after warmup
        log("Clearing RabbitMQ queues after warmup...")
        clear_rabbitmq_script = os.path.join(os.path.dirname(SCRIPT_DIR), 'clear-rabbitmq.sh')
        subprocess.run(['bash', clear_rabbitmq_script], check=False, capture_output=True)
        log("✓ RabbitMQ queues cleared")

        # Reset counters for actual test
        with test_state.received_lock:
            test_state.received_event_count = 0
        test_state.throughput_samples.clear()
        test_state.api_throughput_samples.clear()

        log("✓ Warmup phase completed - service is now at steady-state")
        log(f"Total warmup time: {time.time() - warmup_start:.1f}s (not included in test metrics)")

    except Exception as e:
        log(f"WARNING: Warmup phase failed - {type(e).__name__}: {e}")
        log("Continuing with test anyway (may see cold-start performance)...")
        # Reset counters even if warmup failed
        with test_state.received_lock:
            test_state.received_event_count = 0
        test_state.throughput_samples.clear()
        test_state.api_throughput_samples.clear()

    # Phase 1: Publish events
    log("\n" + "=" * 80)
    log("PHASE 1: PUBLISHING EVENTS")
    log("=" * 80)

    try:
        # Setup publisher connection
        credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
        parameters = pika.ConnectionParameters(
            host=RABBITMQ_HOST,
            port=RABBITMQ_PORT,
            credentials=credentials
        )

        log(f"Connecting to RabbitMQ at {RABBITMQ_HOST}:{RABBITMQ_PORT}...")
        connection = pika.BlockingConnection(parameters)
        channel = connection.channel()

        # Declare exchange
        channel.exchange_declare(
            exchange=EXCHANGE_NAME,
            exchange_type='topic',
            durable=True
        )
        log(f"Exchange '{EXCHANGE_NAME}' declared")

        # Start consumer in a separate thread
        log("Starting consumer thread...")
        consumer_thread = threading.Thread(target=consume_events, args=(test_state,), daemon=True)
        consumer_thread.start()

        # Give consumer time to set up
        time.sleep(CONSUMER_SETUP_DELAY_SEC)

        phase_timestamps["phase1_start"] = time.time() - total_start_time
        publish_time = publish_events(channel)
        phase_timestamps["phase1_end"] = time.time() - total_start_time
        connection.close()

    except Exception as e:
        log(f"ABORTING PHASE 1: Cannot reach RabbitMQ - {type(e).__name__}: {e}")
        log("Skipping to next phase...")

    # Phase 2: Wait for events to be consumed
    log("\n" + "=" * 80)
    log("PHASE 2: WAITING FOR EVENTS")
    log("=" * 80)

    if publish_time > 0:
        phase_timestamps["phase2_start"] = time.time() - total_start_time
        consume_start_time = time.time()

        # Wait for all events to be received with timeout
        timeout_seconds = 120  # Increased timeout to allow for slower consumption
        elapsed = 0
        while True:
            with test_state.received_lock:
                count = test_state.received_event_count

            if count >= NUM_EVENTS or elapsed >= timeout_seconds:
                break

            time.sleep(0.1)
            elapsed = time.time() - consume_start_time

        consume_time = time.time() - consume_start_time
        phase_timestamps["phase2_end"] = time.time() - total_start_time

        with test_state.received_lock:
            final_count = test_state.received_event_count

        if final_count < NUM_EVENTS:
            log(f"WARNING: Only received {final_count}/{NUM_EVENTS} events after {timeout_seconds}s timeout")
        else:
            log(f"Received all {NUM_EVENTS} events in {consume_time:.3f} seconds")

        # Wait for consumer thread to finish
        if consumer_thread is not None:
            consumer_thread.join(timeout=2)
    else:
        log("SKIPPING PHASE 2: Publishing phase was aborted")

    # Phase 3: Call API
    log("\n" + "=" * 80)
    log("PHASE 3: CALLING API")
    log("=" * 80)
    phase_timestamps["phase3_start"] = time.time() - total_start_time

    # Call API using k6
    k6_script_path = os.path.join(SCRIPT_DIR, 'api-load-test.js')
    api_time, api_success, api_errors = run_k6_test(
        k6_script_path,
        API_URL,
        API_DURATION,
        API_CONCURRENT_WORKERS,
        test_state.api_throughput_samples
    )

    phase_timestamps["phase3_end"] = time.time() - total_start_time

    # Summary
    total_time = time.time() - total_start_time
    log("\n" + "=" * 80)
    log("TEST SUMMARY")
    log("=" * 80)
    log(f"Phase 1 (Publish):  {publish_time:.3f} seconds")
    log(f"Phase 2 (Consume):  {consume_time:.3f} seconds")
    log(f"Phase 3 (API):      {api_time:.3f} seconds")
    log(f"Total runtime:      {total_time:.3f} seconds")
    log("=" * 80)

    # Wait briefly to ensure final monitoring samples are captured
    if monitor is not None:
        log("\nWaiting for final monitoring samples...")
        time.sleep(FINAL_MONITORING_DELAY_SEC)  # Wait to capture final samples

    # Generate consistent timestamp for all output files
    test_timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')

    # Get and sanitize program name for filenames
    program_name = ""
    if monitor is not None and monitor.process_name is not None:
        program_name = sanitize_filename(monitor.process_name)
        log(f"\nUsing program name in filenames: {program_name}")

    # Stop process monitoring and save metrics
    if monitor is not None:
        log("\n" + "=" * 80)
        log("STOPPING PROCESS MONITORING")
        log("=" * 80)
        resource_filename = get_report_filename(test_timestamp, "resource-metrics.json", program_name)
        monitor.save_report(resource_filename)

    # Stop RabbitMQ monitoring and save metrics
    if rabbitmq_monitor is not None:
        log("\n" + "=" * 80)
        log("STOPPING RABBITMQ MONITORING")
        log("=" * 80)
        rabbitmq_filename = get_report_filename(test_timestamp, "rabbitmq-metrics.json", program_name)
        rabbitmq_monitor.save_report(rabbitmq_filename)

    # Stop PostgreSQL monitoring and save metrics
    if postgres_monitor is not None:
        log("\n" + "=" * 80)
        log("STOPPING POSTGRESQL MONITORING")
        log("=" * 80)
        postgres_filename = get_report_filename(test_timestamp, "postgres-metrics.json", program_name)
        postgres_monitor.save_report(postgres_filename)

    # Stop system-wide monitoring and save metrics
    if system_wide_monitor is not None:
        log("\n" + "=" * 80)
        log("STOPPING SYSTEM-WIDE MONITORING")
        log("=" * 80)
        system_filename = get_report_filename(test_timestamp, "system-metrics.json", program_name)
        system_wide_monitor.save_report(system_filename)

    # Save throughput metrics
    if test_state.throughput_samples:
        log("\n" + "=" * 80)
        log("SAVING EVENT THROUGHPUT METRICS")
        log("=" * 80)
        throughput_filename = get_report_filename(test_timestamp, "events-throughput.json", program_name)
        save_throughput_report(throughput_filename, test_state.throughput_samples, metric_name="events")

    # Save API throughput metrics
    if test_state.api_throughput_samples:
        log("\n" + "=" * 80)
        log("SAVING API THROUGHPUT METRICS")
        log("=" * 80)
        api_throughput_filename = get_report_filename(test_timestamp, "api-throughput.json", program_name)
        save_throughput_report(api_throughput_filename, test_state.api_throughput_samples, metric_name="calls")

    # Gather system information and write report to file
    log("\nGathering system information...")
    system_info = get_system_info()

    # Prepare monitor info for report
    monitor_info = None
    if monitor is not None and monitor.process_name is not None:
        monitor_info = {
            "name": monitor.process_name,
            "pid": monitor.pid,
            "port": monitor.port
        }

    report_filename = get_report_filename(test_timestamp, "json", program_name)
    write_test_report(
        report_filename,
        publish_time,
        consume_time,
        api_time,
        total_time,
        api_success,
        api_errors,
        system_info,
        NUM_EVENTS,
        API_DURATION,
        API_CONCURRENT_WORKERS,
        EXCHANGE_NAME,
        CONSUMER_QUEUE,
        API_URL,
        PUBLISH_EVENT_TYPE,
        CONSUME_EVENT_TYPE,
        monitor_info,
        phase_timestamps
    )

    # Generate metrics chart
    log("\n" + "=" * 80)
    log("GENERATING METRICS CHART")
    log("=" * 80)
    chart_filename = get_report_filename(test_timestamp, "chart.png", program_name)
    throughput_file = get_report_filename(test_timestamp, "events-throughput.json", program_name)
    api_throughput_file = get_report_filename(test_timestamp, "api-throughput.json", program_name)
    resource_file = get_report_filename(test_timestamp, "resource-metrics.json", program_name)
    rabbitmq_file = get_report_filename(test_timestamp, "rabbitmq-metrics.json", program_name)
    postgres_file = get_report_filename(test_timestamp, "postgres-metrics.json", program_name)
    system_file = get_report_filename(test_timestamp, "system-metrics.json", program_name)

    generate_metrics_chart(
        chart_filename,
        throughput_file,
        api_throughput_file,
        resource_file,
        report_filename,
        test_timestamp,
        rabbitmq_file,
        postgres_file,
        system_file
    )


if __name__ == '__main__':
    main()
