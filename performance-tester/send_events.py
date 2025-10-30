#!/usr/bin/env python3
"""
Script to send instrument.status.changed events to RabbitMQ
"""

import pika
import json
import uuid
from datetime import datetime, timezone
import time
import random
import argparse
import os

# RabbitMQ configuration - read from environment variables with defaults
RABBITMQ_HOST = os.getenv('RABBITMQ_HOST', 'localhost')
RABBITMQ_PORT = int(os.getenv('RABBITMQ_PORT', '5672'))
RABBITMQ_USER = os.getenv('RABBITMQ_USER', 'admin')
RABBITMQ_PASS = os.getenv('RABBITMQ_PASS', 'admin')
EXCHANGE_NAME = os.getenv('RABBITMQ_EXCHANGE', 'referenceservice.comparison')
EVENT_TYPE = os.getenv('RABBITMQ_EVENT_TYPE', 'instrument.status.changed')

# Sample device IDs and statuses
DEVICE_IDS = ['DEVICE-001', 'DEVICE-002', 'DEVICE-003', 'DEVICE-004', 'DEVICE-005']
STATUSES = ['IDLE', 'RUNNING', 'ERROR', 'MAINTENANCE', 'OFFLINE']


def create_cloudevent(device_id, previous_status, current_status):
    """Create a CloudEvents v1.0 event"""
    event_time = datetime.now(timezone.utc).isoformat()
    return {
        # CloudEvents header fields
        "id": str(uuid.uuid4()),
        "specversion": "1.0",
        "source": "urn:uuid:python-event-generator",
        "type": EVENT_TYPE,
        "time": event_time,
        "privacyrelevant": False,
        "datacontenttype": "application/json",
        "dataschema": "https://schemas.joancomasfdz.com/schemas/events-catalog/instrument.status.changed.schema.json",
        "kind": "event",

        # Event payload
        "data": {
            "deviceId": device_id,
            "previousStatus": previous_status,
            "currentStatus": current_status
        }
    }


def send_event(channel, event, verbose=True):
    """Send event to RabbitMQ exchange"""
    message = json.dumps(event)
    channel.basic_publish(
        exchange=EXCHANGE_NAME,
        routing_key=EVENT_TYPE,
        body=message,
        properties=pika.BasicProperties(
            content_type='application/json',
            delivery_mode=2  # make message persistent
        )
    )
    if verbose:
        timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S.%f')[:-3]
        print(f"[{timestamp}] Sent event: {event['data']['deviceId']} {event['data']['previousStatus']} -> {event['data']['currentStatus']}")


def send_multiple_events(channel, count, delay=0, verbose=True):
    """
    Send multiple events to RabbitMQ

    Args:
        channel: RabbitMQ channel
        count: Number of events to send
        delay: Delay between events in seconds (0 = as fast as possible)
        verbose: Print progress messages

    Returns:
        Elapsed time in seconds
    """
    if verbose:
        print(f"\nSending {count} event(s)...")
        print("-" * 60)

    start_time = time.time()

    # Use two alternating statuses for each device
    status_pairs = [('IDLE', 'RUNNING'), ('RUNNING', 'ERROR'), ('ERROR', 'IDLE')]
    device_states = {
        device_id: {'current': random.choice(STATUSES), 'pair_index': random.randint(0, len(status_pairs) - 1)}
        for device_id in DEVICE_IDS
    }

    for i in range(count):
        # Pick a random device
        device_id = random.choice(DEVICE_IDS)
        device_state = device_states[device_id]

        # Get the two statuses to alternate between
        status_pair = status_pairs[device_state['pair_index']]
        previous_status = device_state['current']

        # Switch to the other status in the pair
        current_status = status_pair[1] if previous_status == status_pair[0] else status_pair[0]

        # Update state
        device_state['current'] = current_status

        # Create and send event
        event = create_cloudevent(device_id, previous_status, current_status)
        send_event(channel, event, verbose=verbose)

        # Wait before sending next event (except for the last one)
        if delay > 0 and i < count - 1:
            time.sleep(delay)

    elapsed_time = time.time() - start_time

    if verbose:
        print("-" * 60)
        print(f"Successfully sent {count} event(s)")
        print(f"Total runtime: {elapsed_time:.3f} seconds")

    return elapsed_time


def main():
    # Parse command line arguments
    parser = argparse.ArgumentParser(description='Send instrument.status.changed events to RabbitMQ')
    parser.add_argument('-n', '--count', type=int, default=1,
                        help='Number of events to send (default: 1)')
    parser.add_argument('-d', '--delay', type=float, default=1.0,
                        help='Delay between events in seconds (0 = as fast as possible, default: 1.0)')
    args = parser.parse_args()

    # Connect to RabbitMQ
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
    parameters = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        port=RABBITMQ_PORT,
        credentials=credentials
    )

    print(f"Connecting to RabbitMQ at {RABBITMQ_HOST}:{RABBITMQ_PORT}...")
    connection = pika.BlockingConnection(parameters)
    channel = connection.channel()

    # Declare exchange (idempotent)
    channel.exchange_declare(
        exchange=EXCHANGE_NAME,
        exchange_type='topic',
        durable=True
    )
    print(f"Exchange '{EXCHANGE_NAME}' declared")

    try:
        send_multiple_events(channel, args.count, delay=args.delay, verbose=True)
    except KeyboardInterrupt:
        print("\n\nStopping event generator...")
    finally:
        connection.close()
        print("Connection closed")


if __name__ == '__main__':
    main()
