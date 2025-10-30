#!/bin/bash

# Master Orchestration Script
# This script runs the complete setup and testing pipeline in non-interactive mode:
# 1. Setup environment (mise, SDKs, tools)
# 2. Verify installation
# 3. Build all services
# 4. Run performance tests
# 5. Generate comparison report

set -euo pipefail

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG_FILE="$SCRIPT_DIR/logs/run-all-setup-test.log"

# Test configuration defaults
EVENTS=2000
DURATION="120s"
NATIVE="--native"

# Generate timestamp for results folder
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS_FOLDER="$SCRIPT_DIR/test-results-${TIMESTAMP}"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_phase() {
    echo ""
    echo -e "${BLUE}========================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}========================================${NC}"
    echo ""
}

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Error handler
error_handler() {
    local exit_code=$?
    log_error "Script failed at phase: $CURRENT_PHASE"
    log_error "Exit code: $exit_code"
    log_info "Check log file for details: $LOG_FILE"
    exit $exit_code
}

trap error_handler ERR

# Main execution
main() {
    log_phase "MASTER ORCHESTRATION - Complete Setup and Test Pipeline"
    log_info "Configuration:"
    log_info "  Events: $EVENTS"
    log_info "  Duration: $DURATION"
    log_info "  Mode: Native builds"
    log_info "  Results: $RESULTS_FOLDER"
    log_info "  Log file: $LOG_FILE"
    echo ""

    # Ensure logs directory exists
    mkdir -p "$(dirname "$LOG_FILE")"

    # Redirect all output to both console and log file
    exec > >(tee -a "$LOG_FILE") 2>&1

    # Phase 1: Setup Environment
    CURRENT_PHASE="Environment Setup"
    log_phase "Phase 1: $CURRENT_PHASE"
    log_info "Running setup-environment.sh in non-interactive mode..."
    if ./scripts/infrastructure/setup/setup-environment.sh --non-interactive; then
        log_success "Environment setup completed successfully"
    else
        log_error "Environment setup failed"
        exit 1
    fi

    # Activate mise for current script session
    log_info "Activating mise for script session..."
    if [[ -f "$HOME/.local/bin/mise" ]]; then
        # Initialize PROMPT_COMMAND to avoid "unbound variable" errors with set -u
        PROMPT_COMMAND="${PROMPT_COMMAND:-}"
        eval "$("$HOME/.local/bin/mise" activate bash)"
        export PATH="$HOME/.local/bin:$PATH"
        log_success "mise activated successfully"
    else
        log_error "mise not found at $HOME/.local/bin/mise"
        exit 1
    fi

    # Phase 2: Verify Environment
    CURRENT_PHASE="Environment Verification"
    log_phase "Phase 2: $CURRENT_PHASE"
    log_info "Running verify-environment.sh..."
    if ./scripts/infrastructure/setup/verify-environment.sh; then
        log_success "Environment verification completed successfully"
    else
        log_error "Environment verification failed"
        exit 1
    fi

    # Phase 3: Build All Services
    CURRENT_PHASE="Build All Services"
    log_phase "Phase 3: $CURRENT_PHASE"
    log_info "Running test-all-builds.sh..."
    if ./scripts/tools/build/test-all-builds.sh; then
        log_success "All builds completed successfully"
    else
        log_error "Build phase failed"
        exit 1
    fi

    # Phase 4: Run Performance Tests
    CURRENT_PHASE="Performance Testing"
    log_phase "Phase 4: $CURRENT_PHASE"
    log_info "Running run-all-tests.sh..."
    if ./scripts/tools/testing/run-all-tests.sh --events "$EVENTS" --duration "$DURATION" $NATIVE --results-folder "$RESULTS_FOLDER"; then
        log_success "Performance tests completed successfully"
    else
        log_error "Performance tests failed"
        exit 1
    fi

    # Completion
    log_phase "ALL PHASES COMPLETED SUCCESSFULLY!"
    log_success "Test results are available in: $RESULTS_FOLDER"
    log_success "Log file: $LOG_FILE"
    echo ""
}

# Run main function
main "$@"
