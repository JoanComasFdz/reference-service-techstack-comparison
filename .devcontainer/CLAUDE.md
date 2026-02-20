# Devcontainer Structure Guide

This document explains the organization and maintenance of the devcontainer configuration for easier understanding and modifications.

## Overview

The devcontainer configuration has been refactored to separate concerns and improve maintainability. All complex commands have been moved to dedicated wrapper scripts with logging.

## File Structure

```
.devcontainer/
├── devcontainer.json           # Main configuration (calls wrapper scripts only)
├── Dockerfile                  # Container image definition
├── postcreate-wrapper.sh       # Runs ONCE after container creation
├── poststart-wrapper.sh        # Runs EVERY TIME container starts
├── setup-claudecode.sh         # Claude Code installation and plugin setup
├── setup-claude-devtools.sh   # Build claude-devtools Docker image from source
├── start-claude-devtools.sh   # Start/restart claude-devtools container
├── ccstatusline.settings.json  # Status line configuration for Claude Code
├── install-k6.sh              # k6 load testing tool installation
├── prepull-images.sh          # Pre-pull Docker images for performance
├── init-firewall.sh           # Firewall rules setup
├── fix-docker-iptables.sh     # Docker iptables chains configuration
├── connect-to-testcontainers-network.sh  # Connect to testcontainers network
├── MIGRATION_PLAN.md          # Migration planning documentation
├── TESTCONTAINERS_IN_DEVCONTAINER.md  # Testcontainers setup guide
└── CLAUDE.md                  # This file
```

## Lifecycle Scripts

### postcreate-wrapper.sh
**When:** Runs ONCE when the container is first created (not on restart)

**What it does:**
1. Configures git safe directory
2. Sets up Claude Code
3. Installs k6 performance testing tool
4. Pre-pulls Docker images for faster startup
5. Builds claude-devtools Docker image (non-fatal)
6. Adds DEVCONTAINER=true environment variable to shell configs
7. Configures mise to skip dotnet installation (system dotnet present)

**Log file:** `/tmp/postcreate.log`

**Modify when:** You need to add new tools, change installation steps, or modify initial setup

### poststart-wrapper.sh
**When:** Runs EVERY TIME the container starts (including after restart)

**What it does:**
1. Runs `init-firewall.sh` - Sets up firewall rules with allowed domains
2. Runs `fix-docker-iptables.sh` - Configures Docker iptables chains
3. Runs `connect-to-testcontainers-network.sh` - Connects container to testcontainers network
4. Runs `connect-to-infrastructure-network.sh` - Connects devcontainer to infrastructure network
5. Runs `start-claude-devtools.sh` - Starts claude-devtools web UI container

**Log file:** `/tmp/poststart.log`

**Modify when:** You need to change firewall rules, Docker networking, or startup behavior

## Adding New Setup Steps

### For one-time setup (runs once at creation):
1. Edit `postcreate-wrapper.sh`
2. Add a new step following the existing pattern:
   ```bash
   log "Step N: Description..."
   if your-command >> "$LOGFILE" 2>&1; then
       log "✓ Step completed"
   else
       EXIT_CODE=$?
       log "✗ Step FAILED with exit code $EXIT_CODE"
       exit $EXIT_CODE
   fi
   ```

### For startup setup (runs every time):
1. Edit `poststart-wrapper.sh`
2. Follow the same pattern as above

## Debugging Failed Setup

When setup fails, check the log files:

```bash
# View postcreate log (one-time setup)
cat /tmp/postcreate.log

# View poststart log (every-time setup)
cat /tmp/poststart.log
```

From the host machine (when container exists but you can't access it):
```bash
# Find container ID
docker ps -a | grep reference-service-techstack-comparison

# View logs
docker exec <container-id> cat /tmp/postcreate.log
docker exec <container-id> cat /tmp/poststart.log
```

## Why This Structure?

**Benefits:**
- ✅ **Easier to maintain** - Edit scripts instead of JSON strings
- ✅ **Better debugging** - Detailed logs show exactly where failures occur
- ✅ **Version control friendly** - Clear diffs when scripts change
- ✅ **Reusable** - Scripts can be tested independently
- ✅ **Documented** - Each step is clearly logged with timestamps

**Previous approach:**
```json
"postCreateCommand": "git config ... && bash script1.sh && bash script2.sh && echo ... >> file"
```
- Hard to read
- Hard to debug (no visibility into which step failed)
- Hard to maintain (long JSON strings)

**Current approach:**
```json
"postCreateCommand": "bash /workspace/.devcontainer/postcreate-wrapper.sh"
```
- Clean and simple
- Wrapper script handles all complexity
- Full logging and error reporting

## Firewall Configuration

The firewall setup (`init-firewall.sh`) restricts outbound network access to only allowed domains for security. This prevents accidental data leaks or unwanted network access.

**Allowed domains include:**
- GitHub (for git operations)
- NuGet, npm, PyPI (package managers)
- Docker Hub (container images)
- VS Code marketplace
- Anthropic API (Claude Code)
- Microsoft/Azure CDN (VS Code extensions)
- Cloudflare (Docker Hub CDN)

**To add new domains:**
Edit `init-firewall.sh` and add to either:
- `cdn_domains` - For CDN-backed services (resolved 5 times for multiple IPs)
- `non_cdn_domains` - For stable domains (resolved once)

## Docker Networking

The devcontainer uses Docker-in-Docker to run the performance testing infrastructure. The networking scripts ensure:
1. The devcontainer can access Docker socket
2. The devcontainer is connected to the testcontainers network
3. Docker iptables chains are properly configured
4. Firewall allows Docker bridge network traffic

## Claude DevTools

[claude-devtools](https://github.com/matt1398/claude-devtools) provides a web-based visualization
of Claude Code session traces. It reads session logs from `~/.claude/` and displays:
- Context reconstruction (token attribution across 7 categories)
- Tool call inspector (syntax-highlighted reads, diffs for edits, bash output)
- Compaction visualization (context window fill/refill cycles)
- Subagent/teammate execution trees
- Cross-session search (Cmd+K)

**How it works:**
- Runs as a sibling Docker container named `reference-service-techstack-comparison-claude-devtools`
- Shares the Claude config volume (`/home/node/.claude`) as read-only
- Image is built from source during `postcreate` (one-time, cached on host Docker daemon)
- Container is started during `poststart` (every restart)

**Access:** `http://localhost:3456`

**Useful commands:**
```bash
# Check if running
docker ps | grep claude-devtools

# View logs
docker logs reference-service-techstack-comparison-claude-devtools

# Restart
docker restart reference-service-techstack-comparison-claude-devtools

# Rebuild image (e.g., to update to latest version)
docker rmi claude-devtools:local
bash /workspace/.devcontainer/setup-claude-devtools.sh
bash /workspace/.devcontainer/start-claude-devtools.sh

# Stop (temporary)
docker stop reference-service-techstack-comparison-claude-devtools

# Remove completely
docker rm -f reference-service-techstack-comparison-claude-devtools
docker rmi claude-devtools:local
```

## Maintenance Checklist

When modifying the devcontainer:

- [ ] Test the change by rebuilding the container (`Dev Containers: Rebuild Container`)
- [ ] Check log files if it fails (`/tmp/postcreate.log`, `/tmp/poststart.log`)
- [ ] Update this CLAUDE.md if you change the structure
- [ ] Commit both the script changes and devcontainer.json together
- [ ] Document any new required tools in the main project CLAUDE.md

## Common Issues

**Issue:** postCreateCommand fails
**Solution:** Check `/tmp/postcreate.log` to see which step failed

**Issue:** postStartCommand fails
**Solution:** Check `/tmp/poststart.log` to see which script failed

**Issue:** Firewall blocks needed domain
**Solution:** Add domain to `init-firewall.sh` (see "Firewall Configuration" above)

**Issue:** Docker networking issues
**Solution:** Check that `--cap-add=NET_ADMIN` and `--cap-add=NET_RAW` are in `runArgs`

**Issue:** Changes to scripts not taking effect
**Solution:** Rebuild the container to pick up workspace file changes
