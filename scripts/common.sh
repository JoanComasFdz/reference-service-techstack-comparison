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

# Logs a section header (for visual separation of phases)
log_section() {
    echo ""
    echo -e "${BLUE}========================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}========================================${NC}"
    echo ""
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
    # Check if mise is already in PATH
    if ! command_exists mise; then
        # Try common installation location
        if [[ -f "$HOME/.local/bin/mise" ]]; then
            log_info "Adding mise to PATH from $HOME/.local/bin"
            export PATH="$HOME/.local/bin:$PATH"
        else
            log_error "mise not found in PATH or at $HOME/.local/bin/mise!"
            log_error "Please install mise: ./setup-environment.sh"
            return 1
        fi
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
# Container Environment Detection
################################################################################

# Default infrastructure hosts (can be overridden by detect_container_environment)
POSTGRES_HOST="${POSTGRES_HOST:-localhost}"
RABBITMQ_HOST="${RABBITMQ_HOST:-localhost}"
INFRASTRUCTURE_NETWORK="infrastructure_default"

# Check if running inside a Docker container
is_inside_container() {
    [[ -f "/.dockerenv" ]] || grep -q docker /proc/1/cgroup 2>/dev/null
}

# Test TCP connectivity to a host:port
# Usage: test_tcp_connectivity host port
test_tcp_connectivity() {
    local host=$1
    local port=$2
    nc -z -w1 "$host" "$port" 2>/dev/null
}

# Get the current container name (if running inside a container)
get_container_name() {
    if [[ -f "/.dockerenv" ]]; then
        # Try to get container name from hostname or docker inspect
        local container_id
        container_id=$(cat /proc/self/cgroup 2>/dev/null | grep -oE '[0-9a-f]{64}' | head -1)
        if [[ -n "$container_id" ]]; then
            docker inspect --format '{{.Name}}' "$container_id" 2>/dev/null | sed 's/^\///'
        else
            hostname
        fi
    fi
}

# Connect current container to the infrastructure network
# This allows containers to reach each other by name
connect_to_infrastructure_network() {
    local container_name
    container_name=$(get_container_name)

    if [[ -z "$container_name" ]]; then
        log_warn "Could not determine container name"
        return 1
    fi

    # Check if already connected
    if docker network inspect "$INFRASTRUCTURE_NETWORK" --format '{{range .Containers}}{{.Name}} {{end}}' 2>/dev/null | grep -q "$container_name"; then
        log_info "Container already connected to $INFRASTRUCTURE_NETWORK network"
        return 0
    fi

    # Try to connect
    log_info "Connecting container '$container_name' to $INFRASTRUCTURE_NETWORK network..."
    if docker network connect "$INFRASTRUCTURE_NETWORK" "$container_name" 2>/dev/null; then
        log_success "Connected to $INFRASTRUCTURE_NETWORK network"
        return 0
    else
        log_warn "Failed to connect to $INFRASTRUCTURE_NETWORK network"
        return 1
    fi
}

# Detect container environment and set appropriate host variables
# This function detects if we're running inside a devcontainer/docker container
# and adjusts POSTGRES_HOST and RABBITMQ_HOST accordingly
#
# Sets global variables:
#   POSTGRES_HOST - either "localhost" or "performancetest-postgres"
#   RABBITMQ_HOST - either "localhost" or "performancetest-rabbitmq"
#
# Usage: detect_container_environment
detect_container_environment() {
    log_info "Detecting container environment..."

    # If not inside a container, use localhost
    if ! is_inside_container; then
        log_info "Running on host system - using localhost for infrastructure"
        POSTGRES_HOST="localhost"
        RABBITMQ_HOST="localhost"
        export POSTGRES_HOST RABBITMQ_HOST
        return 0
    fi

    log_info "Running inside a container - checking infrastructure connectivity"

    # First, try localhost (works if containers share network namespace or ports are mapped)
    if test_tcp_connectivity "localhost" 5432; then
        log_info "Infrastructure reachable via localhost"
        POSTGRES_HOST="localhost"
        RABBITMQ_HOST="localhost"
        export POSTGRES_HOST RABBITMQ_HOST
        return 0
    fi

    log_info "localhost:5432 not reachable - checking container network"

    # Try container names (works if on same Docker network)
    if test_tcp_connectivity "performancetest-postgres" 5432; then
        log_info "Infrastructure reachable via container names"
        POSTGRES_HOST="performancetest-postgres"
        RABBITMQ_HOST="performancetest-rabbitmq"
        export POSTGRES_HOST RABBITMQ_HOST
        return 0
    fi

    # Not reachable by container name - try to connect to the infrastructure network
    log_info "Container names not resolvable - attempting to join infrastructure network"

    if connect_to_infrastructure_network; then
        # Wait a moment for DNS to propagate
        sleep 1

        # Verify connectivity after joining
        if test_tcp_connectivity "performancetest-postgres" 5432; then
            log_success "Infrastructure now reachable via container names"
            POSTGRES_HOST="performancetest-postgres"
            RABBITMQ_HOST="performancetest-rabbitmq"
            export POSTGRES_HOST RABBITMQ_HOST
            return 0
        fi
    fi

    # Last resort: try to get container IP directly
    local postgres_ip
    postgres_ip=$(docker inspect performancetest-postgres --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' 2>/dev/null | head -1)

    if [[ -n "$postgres_ip" ]] && test_tcp_connectivity "$postgres_ip" 5432; then
        log_warn "Using container IP address directly (less reliable)"
        POSTGRES_HOST="$postgres_ip"
        local rabbitmq_ip
        rabbitmq_ip=$(docker inspect performancetest-rabbitmq --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' 2>/dev/null | head -1)
        RABBITMQ_HOST="${rabbitmq_ip:-$postgres_ip}"
        export POSTGRES_HOST RABBITMQ_HOST
        return 0
    fi

    log_error "Could not establish connectivity to infrastructure containers"
    log_error "Please ensure Docker infrastructure is running:"
    log_error "  docker-compose -f scripts/infrastructure/docker-compose.yml up -d"
    return 1
}

################################################################################
# RabbitMQ Queue Management
################################################################################

# Unbind a queue from an exchange
# Usage: unbind_queue <queue_name> <exchange_name> <routing_key> [vhost]
# Returns: 0 on success, 1 on failure (logs warning but does not exit)
unbind_queue() {
    local queue_name=$1
    local exchange_name=$2
    local routing_key=$3
    local vhost=${4:-/}
    local container=${RABBITMQ_CONTAINER:-performancetest-rabbitmq}
    local user=${RABBITMQ_USER:-admin}
    local pass=${RABBITMQ_PASSWORD:-admin}

    if [[ -z "$queue_name" || -z "$exchange_name" || -z "$routing_key" ]]; then
        log_warn "unbind_queue: missing required arguments (queue=$queue_name, exchange=$exchange_name, routing_key=$routing_key)"
        return 1
    fi

    log_info "Unbinding queue '$queue_name' from exchange '$exchange_name' (routing_key=$routing_key)"

    if docker exec "$container" rabbitmqadmin -u "$user" -p "$pass" -V "$vhost" \
        delete binding source="$exchange_name" destination_type=queue destination="$queue_name" properties_key="$routing_key" 2>/dev/null; then
        log_success "Unbound queue '$queue_name' from exchange '$exchange_name'"
        return 0
    else
        log_warn "Failed to unbind queue '$queue_name' (may not exist or already unbound)"
        return 1
    fi
}

# Delete a queue from RabbitMQ
# Usage: delete_queue <queue_name> [vhost]
# Returns: 0 on success, 1 on failure (logs warning but does not exit)
delete_queue() {
    local queue_name=$1
    local vhost=${2:-/}
    local container=${RABBITMQ_CONTAINER:-performancetest-rabbitmq}
    local user=${RABBITMQ_USER:-admin}
    local pass=${RABBITMQ_PASSWORD:-admin}

    if [[ -z "$queue_name" ]]; then
        log_warn "delete_queue: missing required argument (queue=$queue_name)"
        return 1
    fi

    log_info "Deleting queue '$queue_name'"

    if docker exec "$container" rabbitmqadmin -u "$user" -p "$pass" -V "$vhost" \
        delete queue name="$queue_name" 2>/dev/null; then
        log_success "Deleted queue '$queue_name'"
        return 0
    else
        log_warn "Failed to delete queue '$queue_name' (may not exist)"
        return 1
    fi
}

################################################################################
# End of common.sh
################################################################################
