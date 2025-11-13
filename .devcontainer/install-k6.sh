#!/bin/bash

# Standalone k6 installation script for dev containers
# Installs k6 load testing tool to ~/.local/bin (no sudo required)

set -e  # Exit on error

echo "========================================="
echo "Installing k6 Load Testing Tool"
echo "========================================="

K6_VERSION="v0.51.0"
K6_BIN="$HOME/.local/bin/k6"

# Check if k6 is already installed
if command -v k6 &> /dev/null; then
    INSTALLED_K6_VERSION=$(k6 version 2>/dev/null | grep -oP 'k6 v\K[0-9.]+' || echo "unknown")
    echo "✓ k6 already installed (version: $INSTALLED_K6_VERSION)"
    exit 0
fi

echo "→ Downloading k6 ${K6_VERSION}..."
K6_URL="https://github.com/grafana/k6/releases/download/${K6_VERSION}/k6-${K6_VERSION}-linux-amd64.tar.gz"

# Create ~/.local/bin if it doesn't exist
mkdir -p "$HOME/.local/bin"

# Download and extract k6 binary
if curl -sL "$K6_URL" | tar -xz -C "$HOME/.local/bin" --strip-components=1 "k6-${K6_VERSION}-linux-amd64/k6" 2>/dev/null; then
    chmod +x "$K6_BIN"

    # Verify installation
    if "$K6_BIN" version &> /dev/null; then
        echo "✓ k6 installed successfully!"
        echo ""
        echo "Installed to: $K6_BIN"
        echo "Version: $("$K6_BIN" version 2>/dev/null | head -1)"
        echo ""
        echo "You can now run integration tests with k6"
    else
        echo "✗ k6 binary downloaded but failed verification"
        exit 1
    fi
else
    echo "✗ Failed to download k6"
    echo ""
    echo "Alternative installation methods:"
    echo "  - sudo snap install k6 (requires sudo)"
    echo "  - Download manually from: https://github.com/grafana/k6/releases"
    exit 1
fi

echo "========================================="
echo "Installation Complete"
echo "========================================="
