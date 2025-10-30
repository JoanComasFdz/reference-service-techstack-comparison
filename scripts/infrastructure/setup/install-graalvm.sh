#!/usr/bin/env bash

# Script to install GraalVM CE builds as additional Java versions in mise
# GraalVM is not supported by mise's core java plugin, so we install it manually

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

# Source shared library
source "${ROOT_DIR}/scripts/common.sh"

MISE_INSTALLS_DIR="$HOME/.local/share/mise/installs/java"

# GraalVM versions to install
GRAALVM_21_VERSION="21.0.2"
GRAALVM_25_VERSION="25.0.1"

# GitHub release URLs
GRAALVM_21_URL="https://github.com/graalvm/graalvm-ce-builds/releases/download/jdk-${GRAALVM_21_VERSION}/graalvm-community-jdk-${GRAALVM_21_VERSION}_linux-x64_bin.tar.gz"
GRAALVM_25_URL="https://github.com/graalvm/graalvm-ce-builds/releases/download/jdk-${GRAALVM_25_VERSION}/graalvm-community-jdk-${GRAALVM_25_VERSION}_linux-x64_bin.tar.gz"

install_graalvm() {
    local version=$1
    local download_url=$2
    local install_name="graalvm-jdk-${version}"
    local install_path="${MISE_INSTALLS_DIR}/${install_name}"

    log "Installing GraalVM for JDK ${version}..."

    # Check if already installed
    if [[ -d "$install_path" ]]; then
        log_warn "GraalVM JDK ${version} is already installed at $install_path"
        log "To reinstall, remove the directory first: rm -rf $install_path"
        return 0
    fi

    # Create mise java installs directory if it doesn't exist
    mkdir -p "$MISE_INSTALLS_DIR"

    # Create temporary directory for download
    local tmp_dir=$(mktemp -d)
    trap "rm -rf $tmp_dir" EXIT

    log "Downloading GraalVM JDK ${version}..."
    log "URL: $download_url"

    if ! curl -L -o "${tmp_dir}/graalvm.tar.gz" "$download_url"; then
        log_error "Failed to download GraalVM JDK ${version}"
        return 1
    fi

    log "Extracting GraalVM JDK ${version}..."
    mkdir -p "$install_path"

    # Extract and strip the top-level directory
    if ! tar -xzf "${tmp_dir}/graalvm.tar.gz" -C "$install_path" --strip-components=1; then
        log_error "Failed to extract GraalVM JDK ${version}"
        rm -rf "$install_path"
        return 1
    fi

    # Verify installation
    if [[ ! -f "${install_path}/bin/java" ]]; then
        log_error "Installation verification failed - java binary not found"
        rm -rf "$install_path"
        return 1
    fi

    # Verify native-image is available
    if [[ ! -f "${install_path}/bin/native-image" ]]; then
        log_warn "native-image not found in ${install_path}/bin"
        log_warn "You may need to install it with: gu install native-image"
    fi

    log "✓ GraalVM JDK ${version} installed successfully"
    log "  Location: $install_path"
    log "  Java: $("${install_path}/bin/java" -version 2>&1 | head -n 1)"
    if [[ -f "${install_path}/bin/native-image" ]]; then
        log "  Native Image: Available"
    fi

    return 0
}

main() {
    echo "=========================================="
    echo "GraalVM Installation for mise"
    echo "=========================================="
    echo ""
    log "This script will install GraalVM CE builds as additional Java versions"
    log "These will be available in mise as 'graalvm-jdk-21.0.2' and 'graalvm-jdk-25.0.1'"
    echo ""

    # Check if mise is installed
    if ! command_exists mise; then
        log_error "mise is not installed or not in PATH"
        log_error "Please run ./setup-environment.sh first"
        exit 1
    fi

    # Install GraalVM for JDK 21
    if ! install_graalvm "$GRAALVM_21_VERSION" "$GRAALVM_21_URL"; then
        log_error "Failed to install GraalVM JDK 21"
        exit 1
    fi
    echo ""

    # Install GraalVM for JDK 25
    if ! install_graalvm "$GRAALVM_25_VERSION" "$GRAALVM_25_URL"; then
        log_error "Failed to install GraalVM JDK 25"
        exit 1
    fi
    echo ""

    echo "=========================================="
    echo "Installation Complete!"
    echo "=========================================="
    echo ""
    log "GraalVM versions installed successfully"
    log ""
    log "Usage:"
    log "  mise use java@graalvm-jdk-21.0.2    # Switch to GraalVM 21"
    log "  mise use java@graalvm-jdk-25.0.1    # Switch to GraalVM 25"
    log "  mise use java@21                     # Switch to regular Java 21"
    log "  mise use java@25                     # Switch to regular Java 25"
    log ""
    log "Aliases configured in .mise.toml:"
    log "  mise use java@graalvm-21            # Alias for GraalVM 21"
    log "  mise use java@graalvm-25            # Alias for GraalVM 25"
    echo ""
}

main "$@"
