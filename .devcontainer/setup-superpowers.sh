#!/bin/bash

# Setup script for Claude Code Superpowers plugin
# Automatically installs the plugin into Claude config directory

CLAUDE_DIR="/home/node/.claude"
PLUGINS_DIR="$CLAUDE_DIR/plugins"
SUPERPOWERS_DIR="$PLUGINS_DIR/superpowers"

echo "Setting up Claude Code Superpowers plugin..."

# Create plugins directory if it doesn't exist
mkdir -p "$PLUGINS_DIR"

# Check if already installed
if [ -d "$SUPERPOWERS_DIR" ]; then
    echo "✓ Superpowers plugin already installed"
    # Update it
    cd "$SUPERPOWERS_DIR" && git pull --quiet 2>/dev/null || echo "  (could not update, but plugin is installed)"
else
    echo "Installing Superpowers plugin..."
    # Clone the plugin repository
    git clone --quiet https://github.com/obra/superpowers.git "$SUPERPOWERS_DIR" 2>/dev/null

    if [ $? -eq 0 ]; then
        echo "✓ Superpowers plugin installed successfully!"
        echo "  Skills and commands are now available in Claude Code"
    else
        echo "✗ Failed to install Superpowers plugin"
        echo "  You can install manually with:"
        echo "    /plugin marketplace add obra/superpowers-marketplace"
        echo "    /plugin install superpowers@superpowers-marketplace"
    fi
fi

# Set proper ownership
chown -R node:node "$CLAUDE_DIR" 2>/dev/null || true
