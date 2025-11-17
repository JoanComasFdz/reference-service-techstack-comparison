# Running Testcontainers in a Devcontainer

This document explains the configuration required to run Testcontainers successfully inside a VS Code devcontainer using Docker socket mounting.

## Overview

Testcontainers is a library that provides lightweight, throwaway instances of databases, message brokers, and other services for integration testing. When running inside a devcontainer with Docker socket mounting, the devcontainer connects to the Testcontainers network to enable direct communication with test containers.

## Architecture

This devcontainer uses **Docker socket mounting** (also called Docker-from-Docker), NOT Docker-in-Docker:

```json
{
  "mounts": [
    "source=/var/run/docker.sock,target=/var/run/docker.sock,type=bind"
  ]
}
```

**How it works:**
- The devcontainer mounts the host's Docker socket
- When code creates Testcontainers, they run on the **host Docker daemon** (not nested)
- The devcontainer itself is connected to the Testcontainers network
- Code inside the devcontainer can reach test containers via container IPs and internal ports

**Why container IPs instead of localhost?**
- `localhost:20000` from host machine → works (port mapping to host)
- `localhost:20000` from inside devcontainer → doesn't work (refers to devcontainer's own localhost)
- Container IP `172.x.x.x:5432` from devcontainer → works (same Docker network)

## Required Components

### 1. Docker Socket Mounting

The devcontainer must mount the host Docker socket:

```json
{
  "mounts": [
    "source=/var/run/docker.sock,target=/var/run/docker.sock,type=bind"
  ],
  "runArgs": [
    "--cap-add=NET_ADMIN",
    "--cap-add=NET_RAW"
  ]
}
```

**Why these capabilities?** The firewall initialization script needs NET_ADMIN and NET_RAW to configure iptables rules.

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

**Why is this needed?** The `init-firewall.sh` script (run before this) configures firewall rules and can affect Docker networking. Even with socket mounting, the host Docker daemon expects certain iptables chains to exist for network isolation and traffic routing. This script ensures those chains are present and allows traffic to the Testcontainers network.

**Required iptables chains:**
- `DOCKER` (nat table) - Port mapping and NAT rules
- `DOCKER` (filter table) - Container traffic filtering
- `DOCKER-FORWARD` - Traffic forwarding between containers and host
- `DOCKER-ISOLATION-STAGE-1/2` - Network isolation between Docker networks
- `DOCKER-USER` - Custom user-defined rules
- `DOCKER-CT` - Connection tracking
- `DOCKER-BRIDGE` - Bridge network rules

**Testcontainers network:** The script explicitly allows traffic to/from `172.19.0.0/16`, which is the default network range used by Testcontainers. This is crucial because the devcontainer will be connected to this network.

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

### 4. Startup Integration and Network Connection

The devcontainer must be connected to the Testcontainers network:

```json
{
  "postStartCommand": "sudo /usr/local/bin/init-firewall.sh && sudo /usr/local/bin/fix-docker-iptables.sh && docker network connect performance-tester-testcontainers-network $(hostname) 2>/dev/null || true"
}
```

**Execution order:**
1. `init-firewall.sh` - Configure firewall rules
2. `fix-docker-iptables.sh` - Recreate Docker chains and allow Testcontainers traffic
3. `docker network connect` - Connect the devcontainer to the Testcontainers network

**Why connect to the network?** This is the key step that enables the devcontainer to communicate with test containers using their container IPs. Without this, the devcontainer would be isolated from the Testcontainers network.

## How It Works

1. **Devcontainer starts** → Mounts host Docker socket at `/var/run/docker.sock`
2. **Firewall initialization** → `init-firewall.sh` sets up network restrictions
3. **iptables configuration** → `fix-docker-iptables.sh` ensures Docker chains exist and allows Testcontainers network traffic
4. **Network connection** → Devcontainer joins the Testcontainers network (`performance-tester-testcontainers-network`)
5. **Testcontainers ready** → Integration tests can create containers on host Docker and communicate with them via container IPs

## ContainerManager Configuration

The `ContainerManager.cs` class detects whether it's running in a devcontainer using the `DEVCONTAINER` environment variable:

```csharp
var isDevContainer = Environment.GetEnvironmentVariable("DEVCONTAINER") == "true";

if (!isDevContainer)
{
    // Host machine: Use localhost with mapped ports
    return container.GetConnectionString(); // e.g., localhost:20000
}

// Devcontainer: Use container IP with internal ports
var containerIp = container.IpAddress; // e.g., 172.19.0.5
var internalPort = 5432; // Internal PostgreSQL port (not mapped 20000)
return $"Host={containerIp};Port={internalPort};...";
```

**Why this matters:**
- **Host machine:** Uses `localhost:20000` (port mapping works)
- **Devcontainer:** Uses `172.19.0.5:5432` (connected to same Docker network)

The `DEVCONTAINER` environment variable should be set in `devcontainer.json`:

```json
{
  "containerEnv": {
    "DEVCONTAINER": "true"
  }
}
```

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
**Solution:** Verify Docker socket is mounted (`/var/run/docker.sock`) and user has permissions (may need to add user to docker group on host)

**Problem:** Cannot reach Docker containers from tests
**Solution:**
- Check that devcontainer is connected to the network: `docker network inspect performance-tester-testcontainers-network`
- Verify `172.19.0.0/16` network range is allowed in iptables rules
- Ensure test code uses container IPs when `DEVCONTAINER=true` environment variable is set

**Problem:** Tests work on host but fail in devcontainer
**Solution:** Verify the `DEVCONTAINER` environment variable is set and ContainerManager is using container IPs instead of localhost ports
