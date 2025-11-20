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
if sudo /usr/local/bin/init-firewall.sh >> "$LOGFILE" 2>&1; then
    log "✓ init-firewall.sh completed successfully"
else
    EXIT_CODE=$?
    log "✗ init-firewall.sh FAILED with exit code $EXIT_CODE"
    log "Check $LOGFILE for details"
    exit $EXIT_CODE
fi

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
if bash /workspace/.devcontainer/connect-to-testcontainers-network.sh >> "$LOGFILE" 2>&1; then
    log "✓ connect-to-testcontainers-network.sh completed successfully"
else
    EXIT_CODE=$?
    log "✗ connect-to-testcontainers-network.sh FAILED with exit code $EXIT_CODE"
    log "Check $LOGFILE for details"
    exit $EXIT_CODE
fi

log "========================================="
log "ALL POSTSTART SCRIPTS COMPLETED SUCCESSFULLY"
log "========================================="
