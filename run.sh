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

# Ensure logs directory exists before anything tries to write to it
mkdir -p "$SCRIPT_DIR/logs"

# Source shared library functions
source "$SCRIPT_DIR/scripts/common.sh"

# Test configuration defaults
EVENTS=1000
DURATION="5s"
NATIVE="--native"

# Generate timestamp for results folder
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS_FOLDER="$SCRIPT_DIR/test-results-${TIMESTAMP}"

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
    log_section "MASTER ORCHESTRATION - Complete Setup and Test Pipeline"
    log_info "Configuration:"
    log_info "  Events: $EVENTS"
    log_info "  Duration: $DURATION"
    log_info "  Mode: Native builds"
    log_info "  Results: $RESULTS_FOLDER"
    log_info "  Log file: $LOG_FILE"
    echo ""

    # Redirect all output to both console and log file
    exec > >(tee -a "$LOG_FILE") 2>&1

    # Phase 1: Setup Environment
    CURRENT_PHASE="Environment Setup"
    log_section "Phase 1: $CURRENT_PHASE"
    log_info "Running setup-environment.sh in non-interactive mode..."
    if ./scripts/infrastructure/setup/setup-environment.sh --non-interactive; then
        log_success "Environment setup completed successfully"
    else
        log_error "Environment setup failed"
        exit 1
    fi

    # Activate mise for current script session
    log_info "Activating mise for script session..."
    if ! activate_mise; then
        log_error "Failed to activate mise"
        exit 1
    fi
    log_success "mise activated successfully"

    # Phase 2: Verify Environment
    CURRENT_PHASE="Environment Verification"
    log_section "Phase 2: $CURRENT_PHASE"
    log_info "Running verify-environment.sh..."
    if ./scripts/infrastructure/setup/verify-environment.sh; then
        log_success "Environment verification completed successfully"
    else
        log_error "Environment verification failed"
        exit 1
    fi

    # Phase 3: Build All Services
    CURRENT_PHASE="Build All Services"
    log_section "Phase 3: $CURRENT_PHASE"
    log_info "Running test-all-builds.sh..."
    if ./scripts/tools/build/test-all-builds.sh; then
        log_success "All builds completed successfully"
    else
        log_error "Build phase failed"
        exit 1
    fi

    # Phase 4: Run Performance Tests
    CURRENT_PHASE="Performance Testing"
    log_section "Phase 4: $CURRENT_PHASE"
    log_info "Running run-all-tests.sh..."
    if ./scripts/tools/testing/run-all-tests.sh --events "$EVENTS" --duration "$DURATION" $NATIVE --results-folder "$RESULTS_FOLDER"; then
        log_success "Performance tests completed successfully"
    else
        log_error "Performance tests failed"
        exit 1
    fi

    # Completion
    log_section "ALL PHASES COMPLETED SUCCESSFULLY!"
    log_success "Test results are available in: $RESULTS_FOLDER"
    log_success "Log file: $LOG_FILE"
    echo ""
}

# Run main function
main "$@"
