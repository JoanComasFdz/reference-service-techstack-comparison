#!/bin/bash

################################################################################
# Uninstall Environment Script
#
# Removes mise and all tools installed by setup-environment.sh
# This allows for clean re-testing of the setup process.
#
# Essential system utilities (git, curl, wget, etc.) are NEVER removed.
# Project-specific tools are prompted individually - you choose what to remove.
#
# Usage: ./uninstall-environment.sh [OPTIONS]
#   --non-interactive    Skip all confirmation prompts (removes everything)
#   --help               Show this help message
################################################################################

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
LOG_FILE="${ROOT_DIR}/logs/uninstall-environment.log"

# Source shared library
source "${ROOT_DIR}/scripts/common.sh"

NON_INTERACTIVE=false

# Global array to track all items to remove (format: "type:value")
declare -a ITEMS_TO_REMOVE=()

# Shell configuration files to clean
readonly SHELL_CONFIGS=(~/.bashrc ~/.bash_profile ~/.bash_login ~/.profile ~/.zshrc ~/.zshenv ~/.zprofile ~/.zlogin)

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --non-interactive)
            NON_INTERACTIVE=true
            shift
            ;;
        --help)
            head -n 12 "$0" | tail -n 8
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Prompt user for confirmation (requires explicit y/n)
confirm() {
    if [[ "$NON_INTERACTIVE" == "true" ]]; then
        return 0
    fi

    local message="$1"
    local reply=""

    while true; do
        # Read from /dev/tty to avoid conflicts with nested loops
        read -p "$message (y/n) " -n 1 -r reply </dev/tty
        echo

        if [[ "$reply" =~ ^[Yy]$ ]]; then
            return 0
        elif [[ "$reply" =~ ^[Nn]$ ]]; then
            return 1
        else
            echo "  Please answer 'y' or 'n'"
        fi
    done
}

# Get disk usage of directory
get_dir_size() {
    if [[ -d "$1" ]]; then
        du -sh "$1" 2>/dev/null | cut -f1
    else
        echo "0"
    fi
}

# Remove pattern from all shell configuration files
clean_shell_configs() {
    local pattern="$1"
    for config in "${SHELL_CONFIGS[@]}"; do
        if [[ -f "$config" ]]; then
            sed -i "/$pattern/d" "$config" 2>/dev/null || true
        fi
    done
}

# Display tool information in standardized format
display_tool_info() {
    local name="$1"
    local version="$2"
    local size="$3"

    log_info "Detected $name installation"
    [[ -n "$version" ]] && echo "  Version: $version"
    [[ -n "$size" ]] && [[ "$size" != "0" ]] && echo "  Size: $size"
    echo ""
}

# Backup shell configuration files
backup_shell_configs() {
    log "Backing up shell configuration files..."
    local backup_dir="${SCRIPT_DIR}/shell_config_backup_$(date +%Y%m%d_%H%M%S)"
    mkdir -p "$backup_dir"

    for config in "${SHELL_CONFIGS[@]}"; do
        if [[ -f "$config" ]]; then
            cp "$config" "$backup_dir/" 2>/dev/null || true
        fi
    done

    log "✓ Backup created at: $backup_dir"
}

# Remove mise installation
uninstall_mise() {
    if command_exists mise || [[ -f "$HOME/.local/bin/mise" ]]; then
        local size=$(get_dir_size "$HOME/.local/share/mise")
        local version=""

        if command_exists mise; then
            version=$(mise --version 2>/dev/null || echo "unknown")
        fi

        log_info "Detected mise installation"
        [[ -n "$version" ]] && echo "  Version: $version"
        echo "  Installation: $HOME/.local/share/mise"
        echo "  Size: $size"
        echo ""

        # List all installed tools
        if command_exists mise; then
            log_info "Currently installed tools:"
            mise list 2>/dev/null | head -n 20 || true
            echo ""
        fi

        log_warn "This will remove mise and ALL managed tools"
        if confirm "Mark mise for removal?"; then
            ITEMS_TO_REMOVE+=("mise")
            log_info "✓ Marked for removal"
        else
            log_info "Keeping mise"
        fi
    else
        log_info "mise not found"
    fi
}

# Remove mise tools (only if keeping mise but wanting to clean tools)
remove_mise_tools() {
    if ! command_exists mise; then
        return 0
    fi

    # Check if mise itself is being removed
    local mise_being_removed=false
    for item in "${ITEMS_TO_REMOVE[@]}"; do
        if [[ "$item" == "mise" ]]; then
            mise_being_removed=true
            break
        fi
    done

    if [[ "$mise_being_removed" == "true" ]]; then
        log_info "mise marked for removal - all tools will be removed automatically"
        return 0
    fi

    log_info "mise is being kept - checking individual tools..."
    echo ""

    # Note: Individual tool removal is handled by mise uninstall command
    # But since mise is being kept, we'll just inform the user
    log_info "To remove individual mise tools, use: mise uninstall <tool>@<version>"
    log_info "To remove all tools: mise prune"
    echo ""
}

# Ask about Maven cache cleanup
ask_remove_maven_cache() {
    if [[ -d "$HOME/.m2/repository" ]]; then
        local size=$(get_dir_size "$HOME/.m2/repository")
        log_info "Detected Maven cache (~/.m2/repository)"
        echo "  Size: $size"
        echo ""

        if confirm "Mark Maven cache for cleanup?"; then
            ITEMS_TO_REMOVE+=("maven_cache:$HOME/.m2/repository")
            log_info "✓ Marked for cleanup"
        else
            log_info "Keeping Maven cache"
        fi
        echo ""
    fi
}

# Ask about Go build cache cleanup
ask_remove_go_cache() {
    if [[ -d "$HOME/.cache/go-build" ]]; then
        local size=$(get_dir_size "$HOME/.cache/go-build")
        log_info "Detected Go build cache (~/.cache/go-build)"
        echo "  Size: $size"
        echo ""

        if confirm "Mark Go build cache for cleanup?"; then
            ITEMS_TO_REMOVE+=("go_build_cache:$HOME/.cache/go-build")
            log_info "✓ Marked for cleanup"
        else
            log_info "Keeping Go build cache"
        fi
        echo ""
    fi

    if [[ -d "$HOME/go" ]]; then
        local size=$(get_dir_size "$HOME/go")
        log_info "Detected Go packages directory (~/go)"
        echo "  Size: $size"
        echo ""

        if confirm "Mark Go packages directory for cleanup?"; then
            ITEMS_TO_REMOVE+=("go_packages:$HOME/go")
            log_info "✓ Marked for cleanup"
        else
            log_info "Keeping Go packages directory"
        fi
        echo ""
    fi
}

# Ask about Rust target directories cleanup
ask_remove_rust_targets() {
    if [[ -d "$SCRIPT_DIR/implementations" ]]; then
        local target_count=$(find "$SCRIPT_DIR/implementations" -type d -name "target" 2>/dev/null | wc -l)
        if [[ $target_count -gt 0 ]]; then
            log_info "Detected Rust target directories in implementations/"
            echo "  Count: $target_count directories"
            echo ""

            if confirm "Mark Rust target directories for cleanup?"; then
                ITEMS_TO_REMOVE+=("rust_targets:$SCRIPT_DIR/implementations")
                log_info "✓ Marked for cleanup"
            else
                log_info "Keeping Rust target directories"
            fi
            echo ""
        fi
    fi
}

# Ask about node_modules cleanup
ask_remove_node_modules() {
    if [[ -d "$SCRIPT_DIR/implementations" ]]; then
        local node_modules_count=$(find "$SCRIPT_DIR/implementations" -type d -name "node_modules" 2>/dev/null | wc -l)
        if [[ $node_modules_count -gt 0 ]]; then
            log_info "Detected node_modules directories in implementations/"
            echo "  Count: $node_modules_count directories"
            echo ""

            if confirm "Mark node_modules directories for cleanup?"; then
                ITEMS_TO_REMOVE+=("node_modules:$SCRIPT_DIR/implementations")
                log_info "✓ Marked for cleanup"
            else
                log_info "Keeping node_modules directories"
            fi
            echo ""
        fi
    fi
}

# Ask about .NET NuGet cache cleanup
ask_remove_dotnet_cache() {
    if [[ -d "$HOME/.nuget/packages" ]]; then
        local size=$(get_dir_size "$HOME/.nuget/packages")
        log_info "Detected .NET NuGet cache (~/.nuget/packages)"
        echo "  Size: $size"
        echo ""

        if confirm "Mark NuGet cache for cleanup?"; then
            ITEMS_TO_REMOVE+=("dotnet_cache:$HOME/.nuget/packages")
            log_info "✓ Marked for cleanup"
        else
            log_info "Keeping NuGet cache"
        fi
        echo ""
    fi
}

# Remove shared Python virtual environment
remove_shared_python_venv() {
    local venv_dir="$SCRIPT_DIR/venv"

    if [[ -d "$venv_dir" ]]; then
        local size=$(get_dir_size "$venv_dir")
        log_info "Detected shared Python virtual environment"
        echo "  Location: $venv_dir"
        echo "  Size: $size"
        echo "  Contains: Python service + performance tester dependencies"
        echo ""

        if confirm "Mark shared Python venv for removal?"; then
            ITEMS_TO_REMOVE+=("python_venv:$venv_dir")
            log_info "✓ Marked for removal"
        else
            log_info "Keeping shared Python venv"
        fi
        echo ""
    else
        log_info "Shared Python venv not found at $venv_dir"
    fi
}

# Ask about k6 removal
ask_remove_k6() {
    if command_exists k6; then
        local k6_version=$(k6 version 2>&1 | head -n 1 || echo "unknown")
        log_info "Detected k6 (performance testing tool)"
        echo "  Version: $k6_version"
        echo "  Installed via: snap"
        echo "  Required for: Performance testing (./run-all-tests.sh)"
        echo ""

        if confirm "Mark k6 for removal?"; then
            ITEMS_TO_REMOVE+=("k6")
            log_info "✓ Marked for removal"
        else
            log_info "Keeping k6"
        fi
        echo ""
    else
        log_info "k6 not found"
    fi
}

# Log preserved packages
log_preserved_packages() {
    log_info "The following components are NEVER removed by this script:"
    echo ""
    echo "  Essential utilities:  git, curl, wget, ca-certificates, gnupg2, dirmngr"
    echo "  APT infrastructure:   software-properties-common, apt-transport-https"
    echo "  Infrastructure:       Docker, docker-compose"
    echo ""
    log_info "The following components are prompted for removal individually:"
    echo ""
    echo "  SDKs/Runtimes:        mise (and all managed tools), GraalVM"
    echo "  Project Tools:        k6 (performance testing), Python venv"
    echo "  Caches:               Maven, Go, Rust, NuGet, node_modules"
    echo "  Infrastructure:       Docker containers, Docker volumes (destructive)"
    echo ""
}

# Clean build artifacts and caches
clean_build_artifacts() {
    log "Cleaning build artifacts and caches..."

    cd "$SCRIPT_DIR"

    # Run existing clean script if available
    if [[ -f "clean-binaries.sh" ]]; then
        bash clean-binaries.sh >> "$LOG_FILE" 2>&1 || true
        log "✓ Build artifacts cleaned (via clean-binaries.sh)"
    fi
}

# Ask about Docker infrastructure cleanup
ask_remove_docker_infrastructure() {
    cd "$SCRIPT_DIR"

    # Check if docker-compose.yml exists
    if [[ ! -f "docker-compose.yml" ]]; then
        log_info "No docker-compose.yml found, skipping Docker infrastructure check"
        return 0
    fi

    # Check if Docker is available
    if ! command_exists docker; then
        log_info "Docker not found, skipping Docker infrastructure check"
        return 0
    fi

    # Check for running containers from this project
    local postgres_running=false
    local rabbitmq_running=false
    local has_stopped_containers=false

    if docker ps | grep -q "performancetest-postgres"; then
        postgres_running=true
    fi

    if docker ps | grep -q "performancetest-rabbitmq"; then
        rabbitmq_running=true
    fi

    if docker ps -a | grep -q "performancetest-"; then
        has_stopped_containers=true
    fi

    # If no containers at all, skip
    if [[ "$postgres_running" == "false" ]] && [[ "$rabbitmq_running" == "false" ]] && [[ "$has_stopped_containers" == "false" ]]; then
        log_info "No Docker infrastructure containers found"
        return 0
    fi

    # Show container status
    if [[ "$postgres_running" == "true" ]] || [[ "$rabbitmq_running" == "true" ]]; then
        log_info "Detected running infrastructure containers:"
        [[ "$postgres_running" == "true" ]] && echo "  • PostgreSQL (performancetest-postgres)"
        [[ "$rabbitmq_running" == "true" ]] && echo "  • RabbitMQ (performancetest-rabbitmq)"
    elif [[ "$has_stopped_containers" == "true" ]]; then
        log_info "Detected stopped infrastructure containers"
    fi
    echo ""

    # Ask about stopping/removing containers
    if confirm "Mark Docker infrastructure containers for removal (docker compose down)?"; then
        ITEMS_TO_REMOVE+=("docker_containers")
        log_info "✓ Marked for removal"

        # Ask about volumes
        echo ""
        log_warn "This will also ask about removing volumes (databases, queues)"
        if confirm "Also mark volumes for removal (THIS WILL DELETE ALL DATA)?"; then
            ITEMS_TO_REMOVE+=("docker_volumes")
            log_info "✓ Volumes marked for removal"
        else
            log_info "Volumes will be preserved"
        fi
    else
        log_info "Keeping Docker infrastructure running"
    fi
    echo ""
}

# Perform all marked removals
perform_all_removals() {
    echo ""
    log "═══════════════════════════════════════════════════════════"
    log "Review: Items Marked for Removal"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Show what will be removed
    local removal_count=${#ITEMS_TO_REMOVE[@]}

    for item in "${ITEMS_TO_REMOVE[@]}"; do
        local type="${item%%:*}"
        local value="${item#*:}"

        case "$type" in
            mise)
                echo "  • mise (and all managed tools)"
                ;;
            k6)
                echo "  • k6 (performance testing tool)"
                ;;
            python_venv)
                echo "  • Shared Python venv ($value)"
                ;;
            maven_cache)
                echo "  • Maven cache (cleanup)"
                ;;
            dotnet_cache)
                echo "  • NuGet cache (cleanup)"
                ;;
            go_build_cache)
                echo "  • Go build cache (cleanup)"
                ;;
            go_packages)
                echo "  • Go packages directory (cleanup)"
                ;;
            rust_targets)
                echo "  • Rust target directories (cleanup)"
                ;;
            node_modules)
                echo "  • node_modules directories (cleanup)"
                ;;
            docker_containers)
                echo "  • Docker infrastructure containers (stop/remove)"
                ;;
            docker_volumes)
                echo "  • Docker volumes (WILL DELETE ALL DATA)"
                ;;
        esac
    done

    if [[ $removal_count -eq 0 ]]; then
        log_info "Nothing selected for removal"
        return 0
    fi

    echo ""

    set +e  # Temporarily disable exit on error for user input
    confirm "Proceed with removal of all marked items?"
    local proceed=$?
    set -e  # Re-enable exit on error

    if [[ $proceed -ne 0 ]]; then
        log_info "Removal cancelled"
        return 0
    fi

    # Now perform actual removals
    echo ""
    log "═══════════════════════════════════════════════════════════"
    log "Executing Removals"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Temporarily disable exit on error for removal process
    # This prevents issues with mise hooks trying to run after mise is removed
    set +e

    for item in "${ITEMS_TO_REMOVE[@]}"; do
        local type="${item%%:*}"
        local value="${item#*:}"

        case "$type" in
            mise)
                log "Removing mise..."

                # Disable mise shell integration in current shell to prevent hook errors
                if command_exists mise; then
                    log_info "Disabling mise hooks in current shell..."
                    # Unset mise hook functions if they exist
                    unset -f _mise_hook 2>/dev/null || true
                    unset -f _mise_hook_precmd 2>/dev/null || true
                    unset -f mise 2>/dev/null || true
                    # Remove from PATH for current shell
                    export PATH="${PATH//:$HOME\/.local\/bin/}"
                    export PATH="${PATH/#$HOME\/.local\/bin:/}"
                fi

                # Remove mise binary
                if [[ -f "$HOME/.local/bin/mise" ]]; then
                    rm -f "$HOME/.local/bin/mise" >> "$LOG_FILE" 2>&1
                fi

                # Remove mise data directory (all installed tools)
                if [[ -d "$HOME/.local/share/mise" ]]; then
                    rm -rf "$HOME/.local/share/mise" >> "$LOG_FILE" 2>&1
                fi

                # Remove mise config directory
                if [[ -d "$HOME/.config/mise" ]]; then
                    rm -rf "$HOME/.config/mise" >> "$LOG_FILE" 2>&1
                fi

                # Remove mise cache directory
                if [[ -d "$HOME/.cache/mise" ]]; then
                    rm -rf "$HOME/.cache/mise" >> "$LOG_FILE" 2>&1
                fi

                # Clean shell configs
                clean_shell_configs 'mise activate'
                clean_shell_configs 'eval "$(mise activate'
                clean_shell_configs '~/.local/bin/mise'
                clean_shell_configs 'export PATH="$HOME/.local/bin:$PATH"  # mise'
                clean_shell_configs '# mise - polyglot tool version manager'
                clean_shell_configs '# Add ~/.local/bin to PATH for mise'

                log "✓ mise removed"
                log_info "Note: Restart your terminal for changes to take full effect"
                ;;
            k6)
                log "Removing k6..."
                if command_exists snap; then
                    sudo snap remove k6 >> "$LOG_FILE" 2>&1
                    log "✓ k6 removed (via snap)"
                else
                    log_warn "snap not available, cannot remove k6"
                fi
                ;;
            python_venv)
                log "Removing shared Python venv..."
                rm -rf "$value" >> "$LOG_FILE" 2>&1
                log "✓ Shared Python venv removed"
                ;;
            maven_cache)
                log "Cleaning Maven cache..."
                rm -rf "$value" >> "$LOG_FILE" 2>&1
                log "✓ Maven cache cleaned"
                ;;
            dotnet_cache)
                log "Cleaning NuGet cache..."
                rm -rf "$value" >> "$LOG_FILE" 2>&1
                log "✓ NuGet cache cleaned"
                ;;
            go_build_cache)
                log "Cleaning Go build cache..."
                rm -rf "$value" >> "$LOG_FILE" 2>&1
                log "✓ Go build cache cleaned"
                ;;
            go_packages)
                log "Cleaning Go packages directory..."
                rm -rf "$value" >> "$LOG_FILE" 2>&1
                log "✓ Go packages directory cleaned"
                ;;
            rust_targets)
                log "Cleaning Rust target directories..."
                find "$value" -type d -name "target" -exec rm -rf {} + 2>/dev/null || true
                log "✓ Rust target directories cleaned"
                ;;
            node_modules)
                log "Cleaning node_modules directories..."
                find "$value" -type d -name "node_modules" -exec rm -rf {} + 2>/dev/null || true
                log "✓ node_modules directories cleaned"
                ;;
            docker_containers)
                log "Stopping Docker infrastructure containers..."
                cd "$SCRIPT_DIR"
                # Try docker compose (v2) first, then docker-compose (v1)
                if command_exists docker && docker compose version >/dev/null 2>&1; then
                    docker compose down >> "$LOG_FILE" 2>&1
                    log "✓ Infrastructure containers stopped (docker compose)"
                elif command_exists docker-compose; then
                    docker-compose down >> "$LOG_FILE" 2>&1
                    log "✓ Infrastructure containers stopped (docker-compose)"
                else
                    log_warn "Neither 'docker compose' nor 'docker-compose' available, skipping"
                fi
                ;;
            docker_volumes)
                log "Removing Docker volumes..."
                cd "$SCRIPT_DIR"
                # Try docker compose (v2) first, then docker-compose (v1)
                if command_exists docker && docker compose version >/dev/null 2>&1; then
                    docker compose down -v >> "$LOG_FILE" 2>&1
                    log "✓ Volumes removed (docker compose)"
                elif command_exists docker-compose; then
                    docker-compose down -v >> "$LOG_FILE" 2>&1
                    log "✓ Volumes removed (docker-compose)"
                else
                    log_warn "Neither 'docker compose' nor 'docker-compose' available, skipping"
                fi
                ;;
        esac
    done

    # Re-enable exit on error
    set -e

    echo ""
    log "✓ All removals complete"
    log_info "Note: You may see some harmless errors above from shell hooks after mise removal"
}

# Main uninstallation flow
main() {
    echo ""
    echo "╔════════════════════════════════════════════════════════════╗"
    echo "║  Reference Service Tech Stack Comparison                   ║"
    echo "║  mise Uninstall Script                                     ║"
    echo "╚════════════════════════════════════════════════════════════╝"
    echo ""

    # Initialize log file
    mkdir -p "$(dirname "$LOG_FILE")"
    echo "Uninstall started at $(date)" > "$LOG_FILE"
    log "Log file: $LOG_FILE"
    echo ""

    # Warning
    log_warn "This script will prompt you individually for each installed component."
    log_info "You will choose what to remove - nothing is removed without confirmation."
    echo ""

    if [[ "$NON_INTERACTIVE" == "false" ]]; then
        read -p "Continue? (y/n) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            log "Uninstall cancelled."
            exit 0
        fi
    fi

    # Backup configurations
    if confirm "Create backup of shell configuration files?"; then
        backup_shell_configs
    fi

    # Inform about preserved components
    echo ""
    log "═══════════════════════════════════════════════════════════"
    log "Preserved System Components"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    log_preserved_packages

    # Collect removal selections
    echo ""
    log "═══════════════════════════════════════════════════════════"
    log "Selecting Items for Removal"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Section 1: SDKs and Runtimes
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    log "Section 1: SDKs and Runtimes"
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    uninstall_mise
    remove_mise_tools
    echo ""

    # Section 2: Project Tools
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    log "Section 2: Project Tools"
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    ask_remove_k6
    remove_shared_python_venv
    echo ""

    # Section 3: Build Caches
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    log "Section 3: Build Caches"
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    ask_remove_maven_cache
    ask_remove_go_cache
    ask_remove_rust_targets
    ask_remove_node_modules
    ask_remove_dotnet_cache
    echo ""

    # Section 4: Infrastructure
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    log "Section 4: Infrastructure (Docker)"
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    ask_remove_docker_infrastructure

    # Execute all removals
    perform_all_removals

    # Clean artifacts
    echo ""
    log "═══════════════════════════════════════════════════════════"
    log "Cleaning Build Artifacts and Caches"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    clean_build_artifacts

    # Summary
    echo ""
    log "═══════════════════════════════════════════════════════════"
    log "Uninstallation Complete!"
    log "═══════════════════════════════════════════════════════════"
    echo ""

    # Note about preserved configuration files
    log_info "Note: .mise.toml configuration file is preserved (tracked in git)"
    log_info "To switch back to SDKMAN approach: git checkout main"
    echo ""

    # Check if any tools are still available after mise removal
    # Note: With proper configuration (Rust paths inside ~/.local/share/mise), all mise-managed
    # tools should be removed. Any remaining tools are system-installed (via apt, snap, etc.)
    local has_tools=false
    command_exists java && has_tools=true
    command_exists mvn && has_tools=true
    command_exists go && has_tools=true
    command_exists rustc && has_tools=true
    command_exists dotnet && has_tools=true
    command_exists bun && has_tools=true
    command_exists python3 && has_tools=true
    command_exists k6 && has_tools=true

    if [[ "$has_tools" == "true" ]]; then
        log_info "System-installed tools detected (not managed by mise, intentionally preserved):"
        command_exists java && echo "  • Java: $(java -version 2>&1 | head -n 1)"
        command_exists mvn && echo "  • Maven: $(mvn -v 2>&1 | head -n 1)"
        command_exists go && echo "  • Go: $(go version 2>&1)"
        command_exists rustc && echo "  • Rust: $(rustc --version 2>&1)"
        command_exists dotnet && echo "  • .NET: $(dotnet --version 2>&1)"
        command_exists bun && echo "  • Bun: $(bun --version 2>&1)"
        command_exists python3 && echo "  • Python: $(python3 --version 2>&1)"
        command_exists k6 && echo "  • k6: $(k6 version 2>&1 | head -n 1)"
    else
        log_info "No system-installed development tools found"
    fi
    echo ""
    log "Note: Restart your terminal for PATH changes to take effect."
    log "Full log available at: $LOG_FILE"
}

# Run main function
main
