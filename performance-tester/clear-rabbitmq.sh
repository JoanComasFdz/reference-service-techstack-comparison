#!/bin/bash

# Clear all messages from all queues in RabbitMQ

echo "========================================="
echo "Clearing RabbitMQ Queues"
echo "========================================="

# Check if RabbitMQ container is running
if ! docker ps | grep -q rabbitmq; then
    echo "ERROR: RabbitMQ container is not running"
    echo "Start it with: docker-compose up -d"
    exit 1
fi

# Get RabbitMQ container ID
RABBITMQ_CONTAINER=$(docker ps -q -f name=rabbitmq)

if [ -z "$RABBITMQ_CONTAINER" ]; then
    echo "ERROR: Could not find RabbitMQ container"
    exit 1
fi

echo "Found RabbitMQ container: $RABBITMQ_CONTAINER"
echo ""

# Get list of all queues
echo "Fetching list of queues..."
QUEUES=$(docker exec "$RABBITMQ_CONTAINER" rabbitmqctl list_queues -q name 2>/dev/null | grep -v "^Timeout" | grep -v "^Listing")

if [ -z "$QUEUES" ]; then
    echo "No queues found or unable to list queues"
    exit 0
fi

echo "Found queues:"
echo "$QUEUES"
echo ""

# Purge each queue
while IFS= read -r queue; do
    if [ -n "$queue" ]; then
        echo "Purging queue: $queue"
        docker exec "$RABBITMQ_CONTAINER" rabbitmqctl purge_queue "$queue" 2>&1

        if [ $? -eq 0 ]; then
            echo "✓ Successfully purged queue: $queue"
        else
            echo "✗ Failed to purge queue: $queue"
        fi
        echo ""
    fi
done <<< "$QUEUES"

echo "========================================="
echo "RabbitMQ queues cleared successfully"
echo "========================================="
