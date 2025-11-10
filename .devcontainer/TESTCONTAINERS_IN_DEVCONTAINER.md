# Running Testcontainers in a Devcontainer

This document explains the configuration required to run Testcontainers successfully inside a VS Code devcontainer with Docker-in-Docker (DinD).

## Overview

Testcontainers is a library that provides lightweight, throwaway instances of databases, message brokers, and other services for integration testing. When running inside a devcontainer with Docker-in-Docker, additional iptables configuration is needed to ensure Docker networking works correctly.

## Required Components

### 1. Docker-in-Docker Feature

The devcontainer must enable Docker-in-Docker support:

```json
{
  "features": {
    "ghcr.io/devcontainers/features/docker-in-docker:2": {
      "version": "latest",
      "moby": true,
      "installDockerBuildx": true,
      "installDockerComposeSwitch": true
    }
  },
  "runArgs": [
    "--privileged"
  ]
}
```

**Why `--privileged`?** Docker-in-Docker requires privileged mode to create containers, networks, and manage iptables rules inside the container.

### 2. iptables Chain Recreation Script

**File:** `fix-docker-iptables.sh`

This script recreates Docker's required iptables chains after the firewall initialization:

```bash
#!/bin/bash
# Create Docker iptables chains for Testcontainers support
# Note: Script is run with sudo, no internal sudo needed

iptables -t nat -N DOCKER 2>/dev/null || true
iptables -t filter -N DOCKER 2>/dev/null || true
iptables -t filter -N DOCKER-FORWARD 2>/dev/null || true
iptables -t filter -N DOCKER-ISOLATION-STAGE-1 2>/dev/null || true
iptables -t filter -N DOCKER-ISOLATION-STAGE-2 2>/dev/null || true
iptables -t filter -N DOCKER-USER 2>/dev/null || true
iptables -t filter -N DOCKER-CT 2>/dev/null || true
iptables -t filter -N DOCKER-BRIDGE 2>/dev/null || true

# Allow Testcontainers network traffic (172.19.0.0/16)
iptables -I OUTPUT 1 -d 172.19.0.0/16 -j ACCEPT
iptables -I INPUT 1 -s 172.19.0.0/16 -j ACCEPT
```

**Why is this needed?** The `init-firewall.sh` script (run before this) flushes all iptables rules including Docker's chains. These chains must be recreated for Docker to create networks and start containers.

**Required iptables chains:**
- `DOCKER` (nat table) - Port mapping and NAT rules
- `DOCKER` (filter table) - Container traffic filtering
- `DOCKER-FORWARD` - Traffic forwarding between containers and host
- `DOCKER-ISOLATION-STAGE-1/2` - Network isolation between Docker networks
- `DOCKER-USER` - Custom user-defined rules
- `DOCKER-CT` - Connection tracking
- `DOCKER-BRIDGE` - Bridge network rules

**Testcontainers network:** The script explicitly allows traffic to/from `172.19.0.0/16`, which is the default network range used by Testcontainers.

### 3. Dockerfile Configuration

The script must be copied into the container and granted passwordless sudo execution:

```dockerfile
# Copy firewall scripts to /usr/local/bin/
COPY init-firewall.sh /usr/local/bin/
COPY fix-docker-iptables.sh /usr/local/bin/

USER root
RUN chmod +x /usr/local/bin/init-firewall.sh /usr/local/bin/fix-docker-iptables.sh && \
  echo "node ALL=(root) NOPASSWD: /usr/local/bin/init-firewall.sh" > /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /usr/local/bin/fix-docker-iptables.sh" >> /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /usr/local/share/docker-init.sh*" >> /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /bin/sh -c * dockerd *" >> /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /usr/bin/pkill *" >> /etc/sudoers.d/node-firewall && \
  chmod 0440 /etc/sudoers.d/node-firewall

USER node
```

**Why passwordless sudo?** The scripts run automatically during container startup via `postStartCommand`. Password prompts would block the startup process.

**Additional sudoers entries:**
- `docker-init.sh*` - Docker-in-Docker feature's initialization script
- `/bin/sh -c * dockerd *` - Allows Docker daemon startup
- `/usr/bin/pkill *` - Allows process management for Docker daemon

### 4. Startup Integration

The script must run after firewall initialization:

```json
{
  "postStartCommand": "sudo /usr/local/share/docker-init.sh sleep 1 && sudo /usr/local/bin/init-firewall.sh && sudo /usr/local/bin/fix-docker-iptables.sh"
}
```

**Execution order:**
1. `docker-init.sh` - Start Docker daemon (provided by docker-in-docker feature)
2. `init-firewall.sh` - Configure firewall (flushes iptables rules)
3. `fix-docker-iptables.sh` - Recreate Docker chains and allow Testcontainers traffic

## How It Works

1. **Container starts** → Docker-in-Docker feature starts the Docker daemon
2. **Firewall initialization** → `init-firewall.sh` sets up network restrictions and flushes existing iptables rules
3. **iptables restoration** → `fix-docker-iptables.sh` recreates Docker's required chains
4. **Testcontainers ready** → Integration tests can now create Docker containers and networks

## Persistence

This configuration runs automatically on every devcontainer startup, so it persists across:
- Container restarts
- VS Code window reloads
- WSL restarts

## Testing

After applying this configuration, integration tests using Testcontainers should work correctly:

```bash
dotnet test
# All Testcontainers-based integration tests should pass
```

## Common Issues

**Problem:** Tests fail with "iptables: No chain/target/match by that name"
**Solution:** Ensure `fix-docker-iptables.sh` runs after `init-firewall.sh` in `postStartCommand`

**Problem:** Permission denied when running Docker commands
**Solution:** Verify `--privileged` flag is set in `runArgs` and sudoers entries are configured

**Problem:** Cannot reach Docker containers from tests
**Solution:** Check that `172.19.0.0/16` network range is allowed in iptables rules
