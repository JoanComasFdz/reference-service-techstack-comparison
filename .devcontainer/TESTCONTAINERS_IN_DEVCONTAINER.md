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
  ]
}
```

### 2. Startup Network Connection

The devcontainer must be connected to the Testcontainers network on startup:

```json
{
  "postStartCommand": "bash /workspace/.devcontainer/poststart-wrapper.sh"
}
```

The `poststart-wrapper.sh` script runs `connect-to-testcontainers-network.sh`, which connects the devcontainer to the Testcontainers network.

**Why connect to the network?** This is the key step that enables the devcontainer to communicate with test containers using their container IPs. Without this, the devcontainer would be isolated from the Testcontainers network.

## How It Works

1. **Devcontainer starts** → Mounts host Docker socket at `/var/run/docker.sock`
2. **Network connection** → Devcontainer joins the Testcontainers network (`performance-tester-testcontainers-network`)
3. **Testcontainers ready** → Integration tests can create containers on host Docker and communicate with them via container IPs

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

**Problem:** Permission denied when running Docker commands
**Solution:** Verify Docker socket is mounted (`/var/run/docker.sock`) and user has permissions (may need to add user to docker group on host)

**Problem:** Cannot reach Docker containers from tests
**Solution:**
- Check that devcontainer is connected to the network: `docker network inspect performance-tester-testcontainers-network`
- Ensure test code uses container IPs when `DEVCONTAINER=true` environment variable is set

**Problem:** Tests work on host but fail in devcontainer
**Solution:** Verify the `DEVCONTAINER` environment variable is set and ContainerManager is using container IPs instead of localhost ports
