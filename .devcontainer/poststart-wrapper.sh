#!/bin/bash
# Wrapper script for postStartCommand with detailed logging
# Logs to /tmp/poststart.log for debugging

set -e

LOGFILE="/tmp/poststart.log"

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOGFILE"
}

log "========================================="
log "POSTSTART WRAPPER STARTING"
log "========================================="

log "Step 1: Running connect-to-testcontainers-network.sh..."
# Fix docker socket permissions (needed for Windows Docker Desktop)
sudo /bin/chmod 666 /var/run/docker.sock 2>/dev/null || true
if bash /workspace/.devcontainer/connect-to-testcontainers-network.sh >> "$LOGFILE" 2>&1; then
    log "✓ connect-to-testcontainers-network.sh completed successfully"
else
    EXIT_CODE=$?
    log "⚠ connect-to-testcontainers-network.sh failed with exit code $EXIT_CODE (non-fatal)"
    log "  Testcontainers network setup is optional - devcontainer will work without it"
    log "  Check $LOGFILE for details if you need testcontainers support"
    # Don't exit - this is not critical for devcontainer operation
fi

log "Step 2: Running connect-to-infrastructure-network.sh..."
if bash /workspace/.devcontainer/connect-to-infrastructure-network.sh >> "$LOGFILE" 2>&1; then
    log "✓ connect-to-infrastructure-network.sh completed successfully"
    log "  PostgreSQL: performancetest-postgres:5432"
    log "  RabbitMQ:   performancetest-rabbitmq:5672"
else
    EXIT_CODE=$?
    log "⚠ connect-to-infrastructure-network.sh failed with exit code $EXIT_CODE (non-fatal)"
    log "  Infrastructure network setup failed - you may need to use host.docker.internal"
    log "  Check $LOGFILE for details"
    # Don't exit - user can manually connect or use host.docker.internal
fi

log "Step 3: Starting claude-devtools container..."
if bash /workspace/.devcontainer/start-claude-devtools.sh >> "$LOGFILE" 2>&1; then
    log "✓ claude-devtools started at http://localhost:3456"
else
    EXIT_CODE=$?
    log "⚠ claude-devtools start FAILED with exit code $EXIT_CODE (non-fatal)"
    log "  To retry: bash /workspace/.devcontainer/start-claude-devtools.sh"
    # Don't exit - this is not critical
fi

log "========================================="
log "ALL POSTSTART SCRIPTS COMPLETED SUCCESSFULLY"
log "========================================="
