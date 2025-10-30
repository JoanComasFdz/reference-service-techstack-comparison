#!/usr/bin/env bash
################################################################################
# Common Library
#
# Shared functions and utilities for environment setup scripts
#
# Usage:
#   source "$(dirname "$0")/lib/common.sh"
#
#   # Optional: Set LOG_FILE before sourcing to enable file logging
#   LOG_FILE="/path/to/logfile.log"
#   source "$(dirname "$0")/lib/common.sh"
################################################################################

# Color definitions
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No Color

################################################################################
# Logging Functions
################################################################################

# Logs a message to stdout and optionally to LOG_FILE if set
log() {
    local message="[$(date +'%Y-%m-%d %H:%M:%S')] $1"
    if [[ -n "${LOG_FILE:-}" ]]; then
        echo -e "${GREEN}${message}${NC}" | tee -a "$LOG_FILE"
    else
        echo -e "${GREEN}${message}${NC}"
    fi
}

# Logs an error message
log_error() {
    local message="[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1"
    if [[ -n "${LOG_FILE:-}" ]]; then
        echo -e "${RED}${message}${NC}" | tee -a "$LOG_FILE"
    else
        echo -e "${RED}${message}${NC}"
    fi
}

# Logs a warning message
log_warn() {
    local message="[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1"
    if [[ -n "${LOG_FILE:-}" ]]; then
        echo -e "${YELLOW}${message}${NC}" | tee -a "$LOG_FILE"
    else
        echo -e "${YELLOW}${message}${NC}"
    fi
}

# Logs an info message
log_info() {
    local message="[$(date +'%Y-%m-%d %H:%M:%S')] INFO: $1"
    if [[ -n "${LOG_FILE:-}" ]]; then
        echo -e "${BLUE}${message}${NC}" | tee -a "$LOG_FILE"
    else
        echo -e "${BLUE}${message}${NC}"
    fi
}

# Logs a success message
log_success() {
    local message="[$(date +'%Y-%m-%d %H:%M:%S')] SUCCESS: $1"
    if [[ -n "${LOG_FILE:-}" ]]; then
        echo -e "${GREEN}${message}${NC}" | tee -a "$LOG_FILE"
    else
        echo -e "${GREEN}${message}${NC}"
    fi
}

################################################################################
# Helper Functions
################################################################################

# Check if a command exists in PATH
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Activate mise and verify it works
activate_mise() {
    if ! command_exists mise; then
        log_error "mise not found in PATH!"
        log_error "Please install mise: ./setup-environment.sh"
        return 1
    fi

    # Activate mise for current shell
    # Initialize PROMPT_COMMAND to avoid "unbound variable" errors with set -u
    PROMPT_COMMAND="${PROMPT_COMMAND:-}"
    eval "$(mise activate bash)"

    # Verify mise is working
    if ! mise --version &> /dev/null; then
        log_error "mise activation failed!"
        log_error "Try running: eval \"\$(mise activate bash)\""
        return 1
    fi

    return 0
}

################################################################################
# Java Version Management
################################################################################

# Detect installed Java versions via mise
# Sets global variables: JAVA_21_VERSION, JAVA_25_VERSION
detect_java_versions() {
    # Find Java 21 version (look for GraalVM or regular Java 21.* versions)
    # Pattern matches both "java 21.x" and "graalvm-jdk-21.x" formats
    JAVA_21_VERSION=$(mise list java 2>/dev/null | grep -E '(java\s+|graalvm-jdk-)21\.' | head -1 | awk '{print $NF}')

    # Find Java 25 version (look for GraalVM or regular Java 25.* versions)
    # Pattern matches both "java 25.x" and "graalvm-jdk-25.x" formats
    JAVA_25_VERSION=$(mise list java 2>/dev/null | grep -E '(java\s+|graalvm-jdk-)25\.' | head -1 | awk '{print $NF}')

    # Debug: Show what was detected
    if [[ -n "$JAVA_21_VERSION" ]]; then
        log_info "Detected Java 21: $JAVA_21_VERSION"
    else
        log_warn "Java 21 not detected"
    fi

    if [[ -n "$JAVA_25_VERSION" ]]; then
        log_info "Detected Java 25: $JAVA_25_VERSION"
    else
        log_warn "Java 25 not detected"
    fi
}

################################################################################
# Shell Configuration Functions
################################################################################

# Detect the user's default shell type
# Returns: "bash", "zsh", or "unknown"
detect_shell() {
    if [[ "$SHELL" == *"zsh"* ]]; then
        echo "zsh"
    elif [[ "$SHELL" == *"bash"* ]]; then
        echo "bash"
    else
        echo "unknown"
    fi
}

# Get the shell configuration file path
# Usage: get_shell_config_file "bash" -> ~/.bashrc
#        get_shell_config_file "zsh"  -> ~/.zshrc
get_shell_config_file() {
    local shell_type=$1
    case "$shell_type" in
        bash)
            echo "$HOME/.bashrc"
            ;;
        zsh)
            echo "$HOME/.zshrc"
            ;;
        *)
            return 1
            ;;
    esac
}

# Check if mise is configured in a shell config file
# Usage: is_mise_configured_in_shell "bash"
#        is_mise_configured_in_shell "zsh"
is_mise_configured_in_shell() {
    local shell_type=$1
    local config_file
    config_file=$(get_shell_config_file "$shell_type")

    if [[ ! -f "$config_file" ]]; then
        return 1
    fi

    if grep -qF "mise activate $shell_type" "$config_file" 2>/dev/null; then
        return 0
    fi

    return 1
}

# Add configuration lines to a shell config file
# Usage: configure_shell_file "$HOME/.bashrc" "bash"
configure_shell_file() {
    local shell_file=$1
    local shell_type=$2

    # Create file with shebang if it doesn't exist
    if [[ ! -f "$shell_file" ]]; then
        echo "#!/bin/$shell_type" > "$shell_file"
        log_info "Created $shell_file"
    fi

    # Function to add a config line with comment if not present
    add_config_line() {
        local line=$1
        local comment=$2

        if ! grep -qF "$line" "$shell_file" 2>/dev/null; then
            echo "" >> "$shell_file"
            echo "$comment" >> "$shell_file"
            echo "$line" >> "$shell_file"
            return 0
        fi
        return 1
    }

    # Add ~/.local/bin to PATH
    if add_config_line 'export PATH="$HOME/.local/bin:$PATH"' "# Add ~/.local/bin to PATH for mise"; then
        log_info "Added ~/.local/bin to PATH in $shell_file"
    fi

    # Add /snap/bin to PATH
    if add_config_line 'export PATH="/snap/bin:$PATH"' "# Add /snap/bin to PATH for snap packages"; then
        log_info "Added /snap/bin to PATH in $shell_file"
    fi

    # Add mise activation
    if add_config_line "eval \"\$(~/.local/bin/mise activate $shell_type)\"" "# mise - polyglot tool version manager"; then
        log_success "Added mise activation to $shell_file"
    fi
}

################################################################################
# End of common.sh
################################################################################
