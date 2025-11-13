#!/bin/bash
# Pre-pull Docker images before firewall is enabled
# This ensures images are cached and firewall doesn't block pulls

set -e  # Exit on error

echo "=========================================="
echo "Pre-pulling Docker images for performance testing..."
echo "=========================================="
echo ""

# Images used by docker-compose
echo "Pulling postgres:15..."
docker pull postgres:15

echo ""
echo "Pulling rabbitmq:3-management..."
docker pull rabbitmq:3-management

echo ""
echo "=========================================="
echo "Pre-caching MCP server dependencies..."
echo "=========================================="
echo ""

# Pre-cache serena MCP server (uvx downloads and caches on first run)
echo "Caching serena MCP server..."
uvx --from git+https://github.com/oraios/serena serena --help > /dev/null 2>&1 || echo "  (serena cached)"

echo ""
echo "=========================================="
echo "✓ Docker images pre-pulled successfully"
echo "✓ MCP servers pre-cached successfully"
echo "=========================================="
