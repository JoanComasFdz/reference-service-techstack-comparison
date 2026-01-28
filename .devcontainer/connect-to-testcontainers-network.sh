#!/bin/bash
# Helper script to connect devcontainer to testcontainers network
# Only ignores "already connected" errors - all other errors are shown

set -e

NETWORK_NAME="performance-tester-testcontainers-network"

# Get the current container ID dynamically using multiple methods
# Method 1: cgroup v1 format (older Docker/containerd)
if [ -z "$CONTAINER_ID" ] && [ -f /proc/self/cgroup ]; then
    CONTAINER_ID=$(grep -oE '[0-9a-f]{64}' /proc/self/cgroup 2>/dev/null | head -1)
fi

# Method 2: cgroup v2 format - look in mountinfo
if [ -z "$CONTAINER_ID" ] && [ -f /proc/self/mountinfo ]; then
    CONTAINER_ID=$(grep -oE '/docker/containers/[0-9a-f]{64}' /proc/self/mountinfo 2>/dev/null | head -1 | grep -oE '[0-9a-f]{64}')
fi

# Method 3: cpuset file
if [ -z "$CONTAINER_ID" ] && [ -f /proc/1/cpuset ]; then
    CONTAINER_ID=$(grep -oE '[0-9a-f]{64}' /proc/1/cpuset 2>/dev/null | head -1)
fi

# Method 4: Query Docker socket directly for container with our hostname
if [ -z "$CONTAINER_ID" ] && [ -S /var/run/docker.sock ]; then
    MY_HOSTNAME=$(hostname)
    CONTAINER_ID=$(docker inspect --format '{{.Id}}' "$MY_HOSTNAME" 2>/dev/null || true)
fi

# Method 5: Fallback to hostname (often the short container ID)
if [ -z "$CONTAINER_ID" ]; then
    CONTAINER_ID=$(hostname)
fi

if [ -z "$CONTAINER_ID" ]; then
    echo "ERROR: Could not determine container ID" >&2
    exit 1
fi

echo "Detected container ID: $CONTAINER_ID"

# Ensure network exists (create if missing)
if ! docker network inspect "$NETWORK_NAME" >/dev/null 2>&1; then
    echo "Creating Docker network: $NETWORK_NAME"
    docker network create "$NETWORK_NAME"
fi

# Try to connect container to network
# Capture both stdout and stderr
OUTPUT=$(docker network connect "$NETWORK_NAME" "$CONTAINER_ID" 2>&1) || {
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

echo "Successfully connected $CONTAINER_ID to $NETWORK_NAME"
