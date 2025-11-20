#!/bin/bash
# Helper script to connect devcontainer to testcontainers network
# Only ignores "already connected" errors - all other errors are shown

set -e

NETWORK_NAME="performance-tester-testcontainers-network"
CONTAINER_NAME="reference-service-techstack-comparison"

# Ensure network exists (create if missing)
if ! docker network inspect "$NETWORK_NAME" >/dev/null 2>&1; then
    echo "Creating Docker network: $NETWORK_NAME"
    docker network create "$NETWORK_NAME"
fi

# Try to connect container to network
# Capture both stdout and stderr
OUTPUT=$(docker network connect "$NETWORK_NAME" "$CONTAINER_NAME" 2>&1) || {
    EXIT_CODE=$?

    # Check if error is "already connected" or "already exists" (expected, ignore)
    if echo "$OUTPUT" | grep -qE "already connected|already exists"; then
        echo "Container already connected to $NETWORK_NAME (OK)"
        exit 0
    fi

    # Any other error is unexpected - show it and fail
    echo "ERROR: Failed to connect container to network:" >&2
    echo "$OUTPUT" >&2
    exit $EXIT_CODE
}

echo "Successfully connected $CONTAINER_NAME to $NETWORK_NAME"
