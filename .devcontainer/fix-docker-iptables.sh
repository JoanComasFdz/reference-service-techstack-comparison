#!/bin/bash
# Fix Docker iptables chains for Testcontainers support
# This script is run after init-firewall.sh via postStartCommand
#
# NOTE: This script is executed with sudo, so no sudo calls are needed inside

echo "Creating Docker iptables chains for Testcontainers..."

# Create all required Docker chains
# (no sudo needed - script is already run as root via postStartCommand)
iptables -t nat -N DOCKER 2>/dev/null || true
iptables -t filter -N DOCKER 2>/dev/null || true
iptables -t filter -N DOCKER-FORWARD 2>/dev/null || true
iptables -t filter -N DOCKER-ISOLATION-STAGE-1 2>/dev/null || true
iptables -t filter -N DOCKER-ISOLATION-STAGE-2 2>/dev/null || true
iptables -t filter -N DOCKER-USER 2>/dev/null || true
iptables -t filter -N DOCKER-CT 2>/dev/null || true
iptables -t filter -N DOCKER-BRIDGE 2>/dev/null || true

# Allow Docker test network traffic (172.19.0.0/16 for Testcontainers)
iptables -I OUTPUT 1 -d 172.19.0.0/16 -j ACCEPT
iptables -I INPUT 1 -s 172.19.0.0/16 -j ACCEPT

# Allow all Docker private subnet ranges (172.16-31.x.x)
# This covers Docker-in-Docker containers that may use non-standard subnets
iptables -I OUTPUT 1 -d 172.16.0.0/12 -j ACCEPT
iptables -I INPUT 1 -s 172.16.0.0/12 -j ACCEPT

echo "Added firewall rules for full Docker subnet range (172.16.0.0/12)"

echo "Docker iptables chains created successfully"

echo ""
echo "Docker Socket Validation"
echo "========================"
# Ensure Unix domain socket access for Docker CLI
# (Already works, but document it explicitly)
echo "Docker socket access: /var/run/docker.sock"
echo "  Unix sockets bypass iptables - no firewall rules needed"
echo ""

# Verify Docker socket is accessible
if docker ps >/dev/null 2>&1; then
    echo "✓ Docker socket accessible"
else
    echo "✗ WARNING: Docker socket not accessible - check Docker-in-Docker configuration"
fi
