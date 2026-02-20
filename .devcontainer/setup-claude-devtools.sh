#!/bin/bash
# Build the claude-devtools Docker image from source
# https://github.com/matt1398/claude-devtools
#
# claude-devtools provides a web UI for visualizing Claude Code session traces.
# It reconstructs execution traces from ~/.claude/ session logs, showing:
# - Context reconstruction (token attribution across categories)
# - Tool call inspector (syntax-highlighted reads, diffs for edits)
# - Compaction visualization (context window fill/refill)
# - Subagent execution trees
#
# The image is built once and cached on the host Docker daemon.
# To force a rebuild: docker rmi claude-devtools:local

set -e

IMAGE_NAME="claude-devtools:local"
REPO_URL="https://github.com/matt1398/claude-devtools.git"
BUILD_DIR="/tmp/claude-devtools-build"

echo "========================================="
echo "Setting up claude-devtools..."
echo "========================================="

# Check if image already exists
if docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
    echo "✓ claude-devtools Docker image already exists"
    echo "  To rebuild: docker rmi $IMAGE_NAME && bash /workspace/.devcontainer/setup-claude-devtools.sh"
    exit 0
fi

# Use container-local docker config (avoids Windows credential helper issues)
export DOCKER_CONFIG="/tmp/docker-config"
mkdir -p "$DOCKER_CONFIG"
echo '{"auths":{}}' > "$DOCKER_CONFIG/config.json"

# Clone the repository (shallow clone for speed)
echo "→ Cloning claude-devtools repository..."
rm -rf "$BUILD_DIR"
git clone --depth 1 --quiet "$REPO_URL" "$BUILD_DIR"

# Build the Docker image
# The Dockerfile is a multi-stage build:
#   1. Builder stage: pnpm install + pnpm standalone:build
#   2. Production stage: node dist-standalone/index.cjs (Fastify server)
echo "→ Building Docker image (this may take a few minutes)..."
docker build -t "$IMAGE_NAME" "$BUILD_DIR"

# Clean up the clone
echo "→ Cleaning up build directory..."
rm -rf "$BUILD_DIR"

echo "✓ claude-devtools Docker image built successfully"
echo "  Image: $IMAGE_NAME"
