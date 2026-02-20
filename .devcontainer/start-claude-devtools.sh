#!/bin/bash
# Start (or restart) the claude-devtools container
# Runs as a sibling Docker container alongside the devcontainer,
# sharing the Claude config volume (read-only).
#
# Accessible at http://localhost:3456
#
# The container reads session logs from the shared Claude config volume
# and serves a web UI for visualizing Claude Code execution traces.

set -e

IMAGE_NAME="claude-devtools:local"
DEVCONTAINER_NAME="reference-service-techstack-comparison"
CONTAINER_NAME="${DEVCONTAINER_NAME}-claude-devtools"
PORT=3456

echo "Starting claude-devtools container..."
echo "  Container name: $CONTAINER_NAME"

# Check if image exists
if ! docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
    echo "⚠ claude-devtools image not found. Run setup-claude-devtools.sh first."
    echo "  Skipping claude-devtools startup (non-fatal)"
    exit 0
fi

# Use container-local docker config (avoids Windows credential helper issues)
export DOCKER_CONFIG="/tmp/docker-config"
mkdir -p "$DOCKER_CONFIG"
echo '{"auths":{}}' > "$DOCKER_CONFIG/config.json"

# Get the Claude config volume name from the devcontainer
CLAUDE_VOLUME=$(docker inspect "$DEVCONTAINER_NAME" \
    --format '{{range .Mounts}}{{if eq .Destination "/home/node/.claude"}}{{.Name}}{{end}}{{end}}' 2>/dev/null)

if [ -z "$CLAUDE_VOLUME" ]; then
    echo "⚠ Could not find Claude config volume on devcontainer '$DEVCONTAINER_NAME'"
    echo "  Skipping claude-devtools startup (non-fatal)"
    exit 0
fi

echo "  Using Claude config volume: $CLAUDE_VOLUME"

# Check if container already exists and is running
if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "✓ claude-devtools is already running at http://localhost:${PORT}"
    exit 0
fi

# Remove stopped container if it exists (from a previous run)
if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "  → Removing stopped claude-devtools container..."
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
fi

# Start the container
echo "  → Starting claude-devtools container..."
docker run -d \
    --name "$CONTAINER_NAME" \
    --restart unless-stopped \
    -v "${CLAUDE_VOLUME}:/data/.claude:ro" \
    -e NODE_ENV=production \
    -e CLAUDE_ROOT=/data/.claude \
    -e HOST=0.0.0.0 \
    -e PORT=$PORT \
    -p ${PORT}:${PORT} \
    "$IMAGE_NAME" >/dev/null

# Verify it started
sleep 2
if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "✓ claude-devtools is running at http://localhost:${PORT}"
else
    echo "⚠ claude-devtools container failed to start"
    echo "  Check logs: docker logs $CONTAINER_NAME"
    # Non-fatal - script exits 0 implicitly at EOF
fi
