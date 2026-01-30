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

log "Step 1: Running init-firewall.sh..."
FIREWALL_OUTPUT=$(sudo /usr/local/bin/init-firewall.sh 2>&1) && {
    echo "$FIREWALL_OUTPUT" >> "$LOGFILE"
    log "✓ init-firewall.sh completed successfully"
} || {
    EXIT_CODE=$?
    echo "$FIREWALL_OUTPUT" >> "$LOGFILE"
    log "✗ init-firewall.sh FAILED with exit code $EXIT_CODE"
    log "Error output:"
    echo "$FIREWALL_OUTPUT" | tail -30
    exit $EXIT_CODE
}

log "Step 2: Running fix-docker-iptables.sh..."
if sudo /usr/local/bin/fix-docker-iptables.sh >> "$LOGFILE" 2>&1; then
    log "✓ fix-docker-iptables.sh completed successfully"
else
    EXIT_CODE=$?
    log "✗ fix-docker-iptables.sh FAILED with exit code $EXIT_CODE"
    log "Check $LOGFILE for details"
    exit $EXIT_CODE
fi

log "Step 3: Running connect-to-testcontainers-network.sh..."
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

log "Step 4: Running connect-to-infrastructure-network.sh..."
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

log "========================================="
log "ALL POSTSTART SCRIPTS COMPLETED SUCCESSFULLY"
log "========================================="
