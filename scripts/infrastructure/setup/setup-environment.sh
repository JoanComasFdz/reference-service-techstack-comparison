#!/bin/bash

################################################################################
# Setup Environment Script
#
# Automates the installation of mise and all required tools/runtimes for the
# Reference Service Tech Stack Comparison project.
#
# Usage: ./setup-environment.sh [OPTIONS]
#   --non-interactive    Skip prompts, install everything
#   --skip-verification  Don't test installations
#   --help               Show this help message
#
# Benefits of mise-based approach:
#   - Faster startup (<100ms vs traditional SDKMAN)
#   - Simpler setup (10-15 min)
#   - Better directory-based version switching
#   - Unified tool management (one tool for all runtimes)
################################################################################

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
LOG_FILE="${ROOT_DIR}/logs/setup-environment.log"

# Source shared library
source "${ROOT_DIR}/scripts/common.sh"
NON_INTERACTIVE=false
SKIP_VERIFICATION=false

# Installation tracking
declare -a FAILED_INSTALLS=()
declare -a SUCCESSFUL_INSTALLS=()

# mise configuration
MISE_INSTALL_PATH="$HOME/.local/bin/mise"
MISE_DATA_DIR="$HOME/.local/share/mise"
MISE_CONFIG_DIR="$HOME/.config/mise"
MISE_CACHE_DIR="$HOME/.cache/mise"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --non-interactive)
            NON_INTERACTIVE=true
            shift
            ;;
        --skip-verification)
            SKIP_VERIFICATION=true
            shift
            ;;
        --help)
            head -n 18 "$0" | tail -n 14
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Check if running on Ubuntu/Debian
check_os() {
    if [[ ! -f /etc/os-release ]]; then
        log_error "Cannot detect OS. This script is designed for Ubuntu/Debian."
        exit 1
    fi

    source /etc/os-release
    if [[ "$ID" != "ubuntu" ]] && [[ "$ID" != "debian" ]]; then
        log_warn "Detected OS: $ID. This script is optimized for Ubuntu/Debian."
        if [[ "$NON_INTERACTIVE" == "false" ]]; then
            read -p "Continue anyway? (y/n) " -n 1 -r
            echo
            if [[ ! $REPLY =~ ^[Yy]$ ]]; then
                exit 1
            fi
        fi
    fi

    log "Detected OS: $PRETTY_NAME"
}

# Prompt user for confirmation
confirm() {
    if [[ "$NON_INTERACTIVE" == "true" ]]; then
        return 0
    fi

    local message="$1"
    read -p "$message (y/n) " -n 1 -r
    echo
    [[ $REPLY =~ ^[Yy]$ ]]
}

# Update package lists
update_apt() {
    log "Updating package lists..."
    sudo apt-get update >> "$LOG_FILE" 2>&1
}

# Install system prerequisites
install_prerequisites() {
    log "Installing system prerequisites..."

    local packages=(
        "build-essential"
        "curl"
        "wget"
        "unzip"
        "zip"
        "ca-certificates"
        "gnupg2"
        "dirmngr"
        "zlib1g-dev"
        "git"
        "software-properties-common"
        "apt-transport-https"
        "pkg-config"
        "libssl-dev"
        "libpq-dev"
    )

    sudo apt-get install -y "${packages[@]}" >> "$LOG_FILE" 2>&1
    if [[ $? -eq 0 ]]; then
        log "✓ System prerequisites installed"
        SUCCESSFUL_INSTALLS+=("System Prerequisites")
    else
        log_error "Failed to install system prerequisites"
        FAILED_INSTALLS+=("System Prerequisites")
        return 1
    fi
}

# Install mise via official installer
install_mise() {
    log "Installing mise..."

    # Check if mise is already installed
    if command_exists mise; then
        local current_version=$(mise --version 2>&1 | head -n 1 | awk '{print $2}')
        log_info "mise is already installed (version: $current_version)"

        if confirm "Reinstall mise?"; then
            log_info "Reinstalling mise..."
        else
            log "✓ Using existing mise installation"
            SUCCESSFUL_INSTALLS+=("mise (existing)")
            return 0
        fi
    fi

    # Download and install mise using official installer
    log_info "Downloading mise installer..."
    if ! curl -fsSL https://mise.run | sh >> "$LOG_FILE" 2>&1; then
        log_error "Failed to download/install mise"
        FAILED_INSTALLS+=("mise")
        return 1
    fi

    # Verify installation
    if [[ ! -f "$MISE_INSTALL_PATH" ]]; then
        log_error "mise binary not found at $MISE_INSTALL_PATH"
        FAILED_INSTALLS+=("mise")
        return 1
    fi

    # Make sure mise is executable
    chmod +x "$MISE_INSTALL_PATH"

    # Add mise to PATH for current session
    export PATH="$HOME/.local/bin:$PATH"

    # Add mise activation to shell config files
    log_info "Configuring shell integrations..."

    # Configure .bashrc
    configure_shell_file "$HOME/.bashrc" "bash"

    # Configure .zshrc if it exists
    if [[ -f "$HOME/.zshrc" ]]; then
        configure_shell_file "$HOME/.zshrc" "zsh"
    fi

    # Activate mise for current session
    eval "$(~/.local/bin/mise activate bash)"

    # Add /snap/bin to PATH for current session (for k6 and other snap packages)
    export PATH="/snap/bin:$PATH"

    # Verify mise works
    if ! command_exists mise; then
        log_error "mise installation failed - command not found"
        FAILED_INSTALLS+=("mise")
        return 1
    fi

    local installed_version=$(mise --version 2>&1 | head -n 1 | awk '{print $2}')
    log "✓ mise installed successfully (version: $installed_version)"
    SUCCESSFUL_INSTALLS+=("mise $installed_version")
}


# Trust existing .mise.toml configuration file
trust_mise_config() {
    log "Validating and trusting .mise.toml configuration..."

    local mise_config_file="${ROOT_DIR}/.mise.toml"

    # Verify .mise.toml exists (should be committed in repository)
    if [[ ! -f "$mise_config_file" ]]; then
        log_error ".mise.toml not found at $mise_config_file"
        log_error "This file should be committed to the repository."
        log_error "Repository may be corrupted or you're not in the correct directory."
        return 1
    fi

    log_info "Found .mise.toml: $mise_config_file"

    # Trust the .mise.toml file (required before using mise)
    log_info "Trusting .mise.toml configuration..."
    if mise trust "$ROOT_DIR" >> "$LOG_FILE" 2>&1; then
        log "✓ .mise.toml trusted successfully"
        log_info "Note: .mise.toml is version-controlled. To customize locally,"
        log_info "      create .mise.local.toml (which is gitignored)."
    else
        log_error "Failed to trust .mise.toml"
        log_error "You may need to run: mise trust $ROOT_DIR"
        return 1
    fi
}

# Install all mise-managed tools
install_all_mise_tools() {
    log "Installing all tools via mise (this will take 10-15 minutes)..."
    log_info "Tools to install: Maven, Go, Rust, .NET 9, Bun, Python 3.13"
    log_info "Note: Java (GraalVM distributions) will be installed separately via install-graalvm.sh"

    # Change to script directory (where .mise.toml is located)
    cd "$ROOT_DIR" || {
        log_error "Failed to change to root directory"
        return 1
    }

    # Verify .mise.toml exists
    if [[ ! -f ".mise.toml" ]]; then
        log_error ".mise.toml not found. This file should be in the repository."
        log_error "Repository may be corrupted or you're not in the correct directory."
        FAILED_INSTALLS+=("mise tools")
        return 1
    fi

    # Install all tools defined in .mise.toml
    log_info "Running: mise install"
    log_info "This may take several minutes depending on your connection..."

    # Run mise install with progress output
    # Use pipefail to catch errors from mise install (not just from tee)
    set +e  # Temporarily disable exit on error
    (
        set -o pipefail
        mise install 2>&1 | tee -a "$LOG_FILE"
    )
    local install_status=$?
    set -e  # Re-enable exit on error

    if [[ $install_status -eq 0 ]]; then
        log "✓ mise tools installed successfully"
        SUCCESSFUL_INSTALLS+=("mise tools")

        # Verify installations
        log_info "Verifying tool installations..."
        mise list >> "$LOG_FILE" 2>&1

        # Count installed tools (excluding missing ones)
        local tool_count=$(mise list 2>/dev/null | grep -v "(missing)" | wc -l || echo "0")
        log_info "Installed tools count: $tool_count"

        return 0
    else
        log_error "Some mise tools failed to install"
        log_error "Check $LOG_FILE for details"

        # Show which tools are missing
        log_error "Missing tools:"
        mise list 2>&1 | grep "(missing)" | tee -a "$LOG_FILE"

        FAILED_INSTALLS+=("Some mise tools")
        return 1
    fi
}

# Install GraalVM distributions manually
install_graalvm() {
    log "Installing GraalVM distributions..."
    log_info "GraalVM is not supported by mise's core java plugin"
    log_info "Installing GraalVM CE builds manually..."

    if [[ -f "$SCRIPT_DIR/install-graalvm.sh" ]]; then
        if "$SCRIPT_DIR/install-graalvm.sh" 2>&1 | tee -a "$LOG_FILE"; then
            log "✓ GraalVM distributions installed successfully"
            SUCCESSFUL_INSTALLS+=("GraalVM JDK 21 & 25")
            return 0
        else
            log_error "Failed to install GraalVM distributions"
            FAILED_INSTALLS+=("GraalVM")
            return 1
        fi
    else
        log_error "install-graalvm.sh script not found"
        FAILED_INSTALLS+=("GraalVM")
        return 1
    fi
}

# Install k6 (system package, not managed by mise)
install_k6() {
    log "Installing k6 load testing tool..."

    if command_exists k6; then
        local k6_version=$(k6 version 2>&1 | head -n 1 | awk '{print $2}')
        log_info "k6 already installed (version: $k6_version)"
        log "✓ k6 available"
        SUCCESSFUL_INSTALLS+=("k6 (existing)")
        return 0
    fi

    log_info "Installing k6 via snap..."
    if sudo snap install k6 >> "$LOG_FILE" 2>&1; then
        log "✓ k6 installed successfully"
        SUCCESSFUL_INSTALLS+=("k6")
    else
        log_warn "Failed to install k6 via snap"
        log_info "You can install k6 manually later: sudo snap install k6"
        FAILED_INSTALLS+=("k6")
        return 1
    fi
}

# Install Python packages globally
install_python_packages() {
    log "Installing Python packages..."

    local perf_tester_requirements="${ROOT_DIR}/performance-tester/requirements.txt"
    local python_service_requirements="${ROOT_DIR}/implementations/python/requirements.txt"

    # Activate mise to get Python from mise
    log_info "Activating mise environment..."
    eval "$(mise activate bash)" >> "$LOG_FILE" 2>&1

    # Verify Python is available from mise
    if ! command -v python &>/dev/null; then
        log_error "Python not found. mise installation may have failed."
        FAILED_INSTALLS+=("Python packages")
        return 1
    fi

    local python_version=$(python --version 2>&1)
    log_info "Using Python: $python_version"

    local install_success=true

    # Install performance-tester packages
    if [[ -f "$perf_tester_requirements" ]]; then
        log_info "Installing performance-tester packages..."
        if python -m pip install -q -r "$perf_tester_requirements" >> "$LOG_FILE" 2>&1; then
            log "✓ Performance-tester packages installed successfully"
        else
            log_error "Failed to install performance-tester packages"
            install_success=false
        fi
    else
        log_warn "performance-tester/requirements.txt not found"
    fi

    # Install Python service packages
    if [[ -f "$python_service_requirements" ]]; then
        log_info "Installing Python service packages..."
        if python -m pip install -q -r "$python_service_requirements" >> "$LOG_FILE" 2>&1; then
            log "✓ Python service packages installed successfully"
        else
            log_error "Failed to install Python service packages"
            install_success=false
        fi
    else
        log_warn "implementations/python/requirements.txt not found"
    fi

    if [[ "$install_success" == "true" ]]; then
        log "✓ All Python packages installed successfully"
        SUCCESSFUL_INSTALLS+=("Python packages")

        log_info "Installed packages:"
        python -m pip list | grep -E "(psutil|pika|sqlalchemy|matplotlib|fastapi|uvicorn|psycopg)" | sed 's/^/    /' | tee -a "$LOG_FILE"
        return 0
    else
        log_error "Some Python packages failed to install"
        FAILED_INSTALLS+=("Python packages")
        return 1
    fi
}

# Verify shell configuration
verify_shell_config() {
    log "Verifying shell configuration..."

    local shell_type
    shell_type=$(detect_shell)

    if [[ "$shell_type" == "unknown" ]]; then
        log_warn "Could not detect shell type"
        log_info "You may need to manually add mise activation to your shell config"
        return 1
    fi

    if is_mise_configured_in_shell "$shell_type"; then
        local config_file
        config_file=$(get_shell_config_file "$shell_type")
        log "✓ mise activation configured in $config_file"
        log_info "Shell configuration verified successfully"
        log_info "New terminal sessions will have mise automatically activated"
        return 0
    else
        log_warn "Could not verify shell configuration"
        log_info "You may need to manually add mise activation to your shell config"
        return 1
    fi
}

# Print installation summary
print_summary() {
    echo ""
    echo "=========================================="
    echo "Installation Summary"
    echo "=========================================="
    echo ""

    if [[ ${#SUCCESSFUL_INSTALLS[@]} -gt 0 ]]; then
        echo -e "${GREEN}✓ Successful installations (${#SUCCESSFUL_INSTALLS[@]}):${NC}"
        for install in "${SUCCESSFUL_INSTALLS[@]}"; do
            echo "  - $install"
        done
        echo ""
    fi

    if [[ ${#FAILED_INSTALLS[@]} -gt 0 ]]; then
        echo -e "${RED}✗ Failed installations (${#FAILED_INSTALLS[@]}):${NC}"
        for install in "${FAILED_INSTALLS[@]}"; do
            echo "  - $install"
        done
        echo ""
    fi

    echo "=========================================="
    echo ""

    if [[ ${#FAILED_INSTALLS[@]} -eq 0 ]]; then
        echo -e "${GREEN}✓✓✓ All installations completed successfully! ✓✓✓${NC}"
        echo ""
        echo -e "${BLUE}=========================================="
        echo "Next Steps"
        echo -e "==========================================${NC}"
        echo ""

        # Detect user's default shell and show appropriate config file
        local shell_type
        shell_type=$(detect_shell)

        echo "  1. Activate mise in your current terminal:"
        echo ""
        if [[ "$shell_type" != "unknown" ]]; then
            local config_file
            config_file=$(get_shell_config_file "$shell_type")
            echo -e "     ${GREEN}source $config_file${NC}"
        else
            echo -e "     ${GREEN}source ~/.bashrc${NC}  # or ~/.zshrc if using zsh"
        fi
        echo ""
        echo "     OR simply open a new terminal window/tab."
        echo "     (mise is configured to activate automatically in new terminals)"
        echo ""
        echo "  2. Verify installation:"
        echo -e "     ${GREEN}./verify-environment.sh${NC}"
        echo ""
        echo "  3. Test that all services build:"
        echo -e "     ${GREEN}./test-all-builds.sh${NC}"
        echo ""
        echo "  4. Run a quick performance test:"
        echo -e "     ${GREEN}./run-all-tests.sh --events 100 --duration 5s${NC}"
        echo ""
        echo "  5. Run full benchmark:"
        echo -e "     ${GREEN}./run-all-tests.sh --events 10000 --duration 30s${NC}"
        echo ""
        echo -e "${BLUE}=========================================="
        echo "mise Commands Reference"
        echo -e "==========================================${NC}"
        echo ""
        echo "Scripts/Automation (recommended):"
        echo -e "  ${GREEN}mise exec java@21 -- mvn package${NC}     # Run command with Java 21"
        echo -e "  ${GREEN}mise exec java@25 -- java -version${NC}   # Run command with Java 25"
        echo ""
        echo "Manual/Interactive (convenience):"
        echo -e "  ${GREEN}mise use java@21${NC}                     # Switch shell to Java 21"
        echo -e "  ${GREEN}mise use java@25${NC}                     # Switch shell to Java 25"
        echo ""
        echo "Information:"
        echo -e "  ${GREEN}mise list${NC}                            # Show installed tools"
        echo -e "  ${GREEN}mise list java${NC}                       # Show Java versions"
        echo -e "  ${GREEN}mise current${NC}                         # Show active versions"
        echo -e "  ${GREEN}mise doctor${NC}                          # Check mise health"
        echo ""
    else
        echo -e "${YELLOW}⚠ Some installations failed.${NC}"
        echo ""
        echo "Check the log for details:"
        echo -e "  ${BLUE}$LOG_FILE${NC}"
        echo ""
        echo "You can try running this script again, or install failed components manually."
        echo ""
    fi
}

# Main installation flow
main() {
    echo "=========================================="
    echo "mise Environment Setup"
    echo "=========================================="
    echo ""
    echo "This script will install mise and all required tools for the"
    echo "Reference Service Tech Stack Comparison project."
    echo ""
    echo "Estimated time: 10-15 minutes"
    echo "Log file: $LOG_FILE"
    echo ""

    # Clear/create log file
    mkdir -p "$(dirname "$LOG_FILE")"
    > "$LOG_FILE"

    # OS check
    check_os

    # Confirm installation
    if ! confirm "Proceed with installation?"; then
        echo "Installation cancelled."
        exit 0
    fi

    echo ""
    log "Starting mise-based environment setup..."
    echo ""

    # ═══════════════════════════════════════════════════════════
    # Section 0: System Prerequisites
    # ═══════════════════════════════════════════════════════════
    log "═══════════════════════════════════════════════════════════"
    log "Section 0: System Prerequisites"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Update package lists
    update_apt || {
        log_error "Failed to update package lists"
        exit 1
    }

    # Install system prerequisites
    install_prerequisites || {
        log_error "Failed to install prerequisites"
        exit 1
    }

    echo ""

    # ═══════════════════════════════════════════════════════════
    # Section 1: SDKs and Runtimes
    # ═══════════════════════════════════════════════════════════
    log "═══════════════════════════════════════════════════════════"
    log "Section 1: SDKs and Runtimes"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Install mise
    install_mise || {
        log_error "Failed to install mise - cannot continue"
        exit 1
    }

    # Trust .mise.toml configuration (version-controlled)
    trust_mise_config || {
        log_error "Failed to trust mise configuration"
        exit 1
    }

    # Install all tools via mise
    install_all_mise_tools || {
        log_warn "Some mise tools failed to install"
    }

    # Install GraalVM distributions manually
    install_graalvm || {
        log_warn "GraalVM installation failed"
    }

    echo ""

    # ═══════════════════════════════════════════════════════════
    # Section 2: Project Tools
    # ═══════════════════════════════════════════════════════════
    log "═══════════════════════════════════════════════════════════"
    log "Section 2: Project Tools"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Install k6 (performance testing tool)
    install_k6

    # Install Python packages globally (for Python service + performance tester)
    install_python_packages

    echo ""

    # ═══════════════════════════════════════════════════════════
    # Section 3: Verification
    # ═══════════════════════════════════════════════════════════
    log "═══════════════════════════════════════════════════════════"
    log "Section 3: Verification"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Verify shell configuration was set up correctly
    verify_shell_config || {
        log_warn "Shell configuration verification had issues (non-critical)"
    }

    echo ""

    # Print summary
    print_summary

    # Exit with appropriate code
    if [[ ${#FAILED_INSTALLS[@]} -eq 0 ]]; then
        exit 0
    else
        exit 1
    fi
}

# Run main function
main
