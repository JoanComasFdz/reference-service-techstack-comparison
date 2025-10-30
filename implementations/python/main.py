from __future__ import annotations

import json
import logging
import os
import threading
import uuid
import uvicorn
from collections.abc import Callable
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from typing import Any

from fastapi import FastAPI
import pika

from models import InstrumentStatus, init_db, SessionLocal

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Constants
EXCHANGE_NAME = "referenceservice.comparison"
QUEUE_NAME = "pythonFastAPI"
ROUTING_KEY_STATUS_CHANGED = "instrument.status.changed"
ROUTING_KEY_KPI_UPDATED = "instrumentstatus.kpi.updated"

# RabbitMQ configuration from environment variables
RABBITMQ_HOST = os.getenv('RABBITMQ_HOST', 'localhost')
RABBITMQ_PORT = int(os.getenv('RABBITMQ_PORT', '5672'))
RABBITMQ_USER = os.getenv('RABBITMQ_USER', 'admin')
RABBITMQ_PASSWORD = os.getenv('RABBITMQ_PASSWORD', 'admin')
RABBITMQ_PREFETCH_COUNT = int(os.getenv('RABBITMQ_PREFETCH_COUNT', '50'))

RABBITMQ_CONFIG: dict[str, Any] = {
    'host': RABBITMQ_HOST,
    'port': RABBITMQ_PORT,
    'credentials': pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASSWORD),
    'prefetch_count': RABBITMQ_PREFETCH_COUNT
}

# Global variables
rabbitmq_connection = None
rabbitmq_channel = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Manage application lifecycle: startup and shutdown"""
    global rabbitmq_connection, rabbitmq_channel

    logger.info("Starting Python FastAPI Reference Service...")

    # Initialize database
    init_db()
    logger.info("Database initialized successfully with SQLAlchemy")

    # Initialize RabbitMQ
    initialize_rabbitmq()

    # Start RabbitMQ consumer in background
    consumer_thread = threading.Thread(target=start_rabbitmq_consumer, daemon=True)
    consumer_thread.start()

    logger.info("Service started successfully on port 8099")

    yield  # Application is running

    # Cleanup on shutdown
    if rabbitmq_channel:
        rabbitmq_channel.close()
    if rabbitmq_connection:
        rabbitmq_connection.close()

    logger.info("Service shutdown complete")


# FastAPI app with lifespan
app = FastAPI(lifespan=lifespan)


class CloudEvent:
    """Base class for CloudEvents v1.0 with extensions"""

    def __init__(self, source: str, event_type: str, data: dict[str, Any]) -> None:
        self.id = str(uuid.uuid4())
        self.specversion = "1.0"
        self.source = source
        self.type = event_type
        self.time = datetime.now(timezone.utc).isoformat()
        self.privacyrelevant = False
        self.datacontenttype = "application/json"
        self.dataschema = f"https://example.com/schemas/events-catalog/{event_type}.schema.json"
        self.kind = "event"
        self.data = data

    def to_dict(self) -> dict[str, Any]:
        return {
            'id': self.id,
            'specversion': self.specversion,
            'source': self.source,
            'type': self.type,
            'time': self.time,
            'privacyrelevant': self.privacyrelevant,
            'datacontenttype': self.datacontenttype,
            'dataschema': self.dataschema,
            'kind': self.kind,
            'data': self.data
        }


def save_instrument_status(device_id: str, previous_status: str, current_status: str) -> int:
    """Save instrument status to database using SQLAlchemy"""
    with SessionLocal() as db:
        try:
            status = InstrumentStatus(
                device_id=device_id,
                previous_status=previous_status,
                current_status=current_status,
                timestamp=datetime.now(timezone.utc)
            )
            db.add(status)
            db.commit()
            db.refresh(status)
            return status.id
        except Exception as e:
            logger.error(f"Error saving to database: {e}")
            db.rollback()
            raise


def publish_kpi_event(device_id: str, current_status: str) -> None:
    """Publish KPI updated event to RabbitMQ"""
    global rabbitmq_channel

    try:
        kpi_event = CloudEvent(
            source="urn:uuid:python-fastapi",
            event_type="instrumentstatus.kpi.updated",
            data={
                'deviceId': device_id,
                'currentStatus': current_status
            }
        )

        message_body = json.dumps(kpi_event.to_dict())

        rabbitmq_channel.basic_publish(
            exchange=EXCHANGE_NAME,
            routing_key=ROUTING_KEY_KPI_UPDATED,
            body=message_body.encode('utf-8')
        )

    except Exception as e:
        logger.error(f"Error publishing KPI event: {e}")


def process_message(ch: Any, method: Any, properties: Any, body: bytes) -> None:
    """Process incoming RabbitMQ message"""
    try:
        message = json.loads(body.decode('utf-8'))

        # Extract data from event
        data = message.get('data', {})
        device_id = data.get('deviceId', '')
        previous_status = data.get('previousStatus', '')
        current_status = data.get('currentStatus', '')

        # Save to database
        save_instrument_status(device_id, previous_status, current_status)

        # Publish KPI event
        publish_kpi_event(device_id, current_status)

        # Acknowledge message after successful processing
        ch.basic_ack(delivery_tag=method.delivery_tag)

    except Exception as e:
        logger.error(f"Error processing message: {e}")
        # Reject and requeue message on failure
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)


def initialize_rabbitmq() -> None:
    """Initialize RabbitMQ connection and start consuming"""
    global rabbitmq_connection, rabbitmq_channel

    try:
        # Create connection (exclude prefetch_count from connection params)
        connection_params = {k: v for k, v in RABBITMQ_CONFIG.items() if k != 'prefetch_count'}
        rabbitmq_connection = pika.BlockingConnection(
            pika.ConnectionParameters(**connection_params)
        )
        rabbitmq_channel = rabbitmq_connection.channel()

        # Declare exchange
        rabbitmq_channel.exchange_declare(
            exchange=EXCHANGE_NAME,
            exchange_type='topic',
            durable=True
        )

        # Declare queue
        rabbitmq_channel.queue_declare(
            queue=QUEUE_NAME,
            durable=True
        )

        # Bind queue to exchange
        rabbitmq_channel.queue_bind(
            queue=QUEUE_NAME,
            exchange=EXCHANGE_NAME,
            routing_key=ROUTING_KEY_STATUS_CHANGED
        )

        # Set prefetch count (QoS)
        rabbitmq_channel.basic_qos(prefetch_count=RABBITMQ_CONFIG['prefetch_count'])

        logger.info(f"RabbitMQ initialized: Exchange={EXCHANGE_NAME}, Queue={QUEUE_NAME}, Prefetch={RABBITMQ_CONFIG['prefetch_count']}")

        # Set up consumer
        rabbitmq_channel.basic_consume(
            queue=QUEUE_NAME,
            on_message_callback=process_message,
            auto_ack=False
        )

        logger.info(f"Started consuming messages from queue: {QUEUE_NAME}")

    except Exception as e:
        logger.error(f"Error initializing RabbitMQ: {e}")
        raise


def start_rabbitmq_consumer() -> None:
    """Start RabbitMQ consumer in a separate thread"""
    try:
        rabbitmq_channel.start_consuming()
    except Exception as e:
        logger.error(f"Error in RabbitMQ consumer: {e}")


@app.get("/kpi")
async def get_kpi() -> dict[str, Any]:
    """Get the latest instrument status from database using SQLAlchemy"""
    with SessionLocal() as db:
        try:
            # Query latest status
            status = db.query(InstrumentStatus).order_by(
                InstrumentStatus.timestamp.desc()
            ).first()

            if status:
                return {
                    'id': status.id,
                    'deviceId': status.device_id,
                    'previousStatus': status.previous_status,
                    'currentStatus': status.current_status,
                    'timestamp': status.timestamp.isoformat()
                }
            else:
                return {}

        except Exception as e:
            logger.error(f"Error retrieving KPI: {e}")
            return {}


if __name__ == "__main__":
    SERVER_PORT = int(os.getenv('SERVER_PORT', '8099'))
    uvicorn.run(app, host="0.0.0.0", port=SERVER_PORT)
