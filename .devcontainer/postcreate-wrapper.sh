#!/bin/bash
# Post-create command wrapper for devcontainer
# This script runs once after the container is created (not on restart)
# Logs to /tmp/postcreate.log for debugging

set -e

LOGFILE="/tmp/postcreate.log"

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOGFILE"
}

log "========================================="
log "POSTCREATE WRAPPER STARTING"
log "========================================="

log "Step 1: Configuring git safe directory..."
if git config --global --add safe.directory /workspace >> "$LOGFILE" 2>&1; then
    log "✓ Git safe directory configured"
else
    EXIT_CODE=$?
    log "✗ Git config FAILED with exit code $EXIT_CODE"
    exit $EXIT_CODE
fi

log "Step 2: Setting up Claude Code..."
if bash /workspace/.devcontainer/setup-claudecode.sh >> "$LOGFILE" 2>&1; then
    log "✓ Claude Code setup completed"
else
    EXIT_CODE=$?
    log "✗ Claude Code setup FAILED with exit code $EXIT_CODE"
    exit $EXIT_CODE
fi

log "Step 3: Installing k6..."
if bash /workspace/.devcontainer/install-k6.sh >> "$LOGFILE" 2>&1; then
    log "✓ k6 installation completed"
else
    EXIT_CODE=$?
    log "✗ k6 installation FAILED with exit code $EXIT_CODE"
    exit $EXIT_CODE
fi

log "Step 4: Pre-pulling Docker images..."
PREPULL_OUTPUT=$(bash /workspace/.devcontainer/prepull-images.sh 2>&1) && {
    echo "$PREPULL_OUTPUT" >> "$LOGFILE"
    log "✓ Docker images pre-pulled"
} || {
    EXIT_CODE=$?
    echo "$PREPULL_OUTPUT" >> "$LOGFILE"
    log "✗ Docker image pre-pull FAILED with exit code $EXIT_CODE"
    log "Error output:"
    echo "$PREPULL_OUTPUT" | tail -20
    exit $EXIT_CODE
}

log "Step 5: Adding DEVCONTAINER=true to shell configs..."
if echo 'export DEVCONTAINER=true' >> /home/node/.zshrc && \
   echo 'export DEVCONTAINER=true' >> /home/node/.bashrc; then
    log "✓ Shell environment variables configured"
else
    EXIT_CODE=$?
    log "✗ Shell config FAILED with exit code $EXIT_CODE"
    exit $EXIT_CODE
fi

log "========================================="
log "ALL POSTCREATE SCRIPTS COMPLETED SUCCESSFULLY"
log "========================================="
