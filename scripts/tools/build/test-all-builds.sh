#!/bin/bash

################################################################################
# Test All Builds Script
#
# Attempts to compile/build all implementations to verify they work.
# This is a quick sanity check that the environment is properly configured.
#
# Usage: ./test-all-builds.sh [OPTIONS]
#   --verbose       Show detailed build output
#   --stop-on-error Stop at first failure
#   --help          Show this help message
################################################################################

set +e  # Don't exit on error - we want to test all implementations

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
LOG_FILE="${ROOT_DIR}/logs/test-all-builds.log"

# Source shared library (no LOG_FILE set = stdout only for logging)
unset LOG_FILE  # Temporarily unset so shared library doesn't log to file
source "${ROOT_DIR}/scripts/common.sh"
LOG_FILE="${ROOT_DIR}/logs/test-all-builds.log"  # Restore LOG_FILE

VERBOSE=false
STOP_ON_ERROR=false
PASSED=0
FAILED=0

# Java version management (mise) - will be populated by detect_java_versions()
JAVA_21_VERSION=""
JAVA_25_VERSION=""

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --verbose)
            VERBOSE=true
            shift
            ;;
        --stop-on-error)
            STOP_ON_ERROR=true
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

# Activate mise using shared library function
if ! activate_mise; then
    log_error "Failed to activate mise"
    exit 1
fi

# Detect Java versions using shared library function
detect_java_versions

# Test build result
test_result() {
    local name="$1"
    local status=$2

    printf "%-35s " "$name"

    if [[ $status -eq 0 ]]; then
        echo -e "[${GREEN}✓ PASS${NC}]"
        ((PASSED++))
    else
        echo -e "[${RED}✗ FAIL${NC}]"
        ((FAILED++))
        if [[ "$STOP_ON_ERROR" == "true" ]]; then
            log_error "Build failed. Check $LOG_FILE for details."
            exit 1
        fi
    fi
}

# Test Java 21 event library
build_java21_events() {
    log_info "Testing Java 21 event library..."
    cd "$ROOT_DIR/implementations/java21.events" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_21_VERSION" -- mvn clean install
    else
        mise exec "java@$JAVA_21_VERSION" -- mvn clean install >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 21 Event Library (java21.events)" $result
    return $result
}

# Test Java 25 event library
build_java25_events() {
    log_info "Testing Java 25 event library..."
    cd "$ROOT_DIR/implementations/java25.events" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_25_VERSION" -- mvn clean install
    else
        mise exec "java@$JAVA_25_VERSION" -- mvn clean install >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 25 Event Library (java25.events)" $result
    return $result
}

# Test Java 21 Spring Boot
test_java21_springboot() {
    log_info "Testing Java 21 Spring Boot..."
    cd "$ROOT_DIR/implementations/java21springboot" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_21_VERSION" -- mvn clean compile
    else
        mise exec "java@$JAVA_21_VERSION" -- mvn clean compile >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 21 Spring Boot (JVM)" $result
    return $result
}

# Test Java 21 Spring Boot Graal
test_java21_springboot_graal() {
    log_info "Testing Java 21 Spring Boot Graal..."
    cd "$ROOT_DIR/implementations/java21springbootgraal" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_21_VERSION" -- mvn clean compile
    else
        mise exec "java@$JAVA_21_VERSION" -- mvn clean compile >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 21 Spring Boot (GraalVM)" $result
    return $result
}

# Test Java 21 Quarkus Graal
test_java21_quarkus() {
    log_info "Testing Java 21 Quarkus Graal..."
    cd "$ROOT_DIR/implementations/java21quarkusgraal" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_21_VERSION" -- mvn clean compile
    else
        mise exec "java@$JAVA_21_VERSION" -- mvn clean compile >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 21 Quarkus (GraalVM)" $result
    return $result
}

# Test Java 25 Spring Boot
test_java25_springboot() {
    log_info "Testing Java 25 Spring Boot..."
    cd "$ROOT_DIR/implementations/java25springboot" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_25_VERSION" -- mvn clean compile
    else
        mise exec "java@$JAVA_25_VERSION" -- mvn clean compile >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 25 Spring Boot (JVM)" $result
    return $result
}

# Test Java 25 Spring Boot Graal
test_java25_springboot_graal() {
    log_info "Testing Java 25 Spring Boot Graal..."
    cd "$ROOT_DIR/implementations/java25springbootgraal" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_25_VERSION" -- mvn clean compile
    else
        mise exec "java@$JAVA_25_VERSION" -- mvn clean compile >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 25 Spring Boot (GraalVM)" $result
    return $result
}

# Test Java 25 Quarkus Graal
test_java25_quarkus() {
    log_info "Testing Java 25 Quarkus Graal..."
    cd "$ROOT_DIR/implementations/java25quarkusgraal" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        mise exec "java@$JAVA_25_VERSION" -- mvn clean compile
    else
        mise exec "java@$JAVA_25_VERSION" -- mvn clean compile >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Java 25 Quarkus (GraalVM)" $result
    return $result
}

# Test .NET 9
test_dotnet9() {
    log_info "Testing .NET 9..."
    cd "$ROOT_DIR/implementations/dotnet9" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        dotnet build
    else
        dotnet build >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result ".NET 9 (JIT)" $result
    return $result
}

# Test .NET 9 AOT
test_dotnet9_aot() {
    log_info "Testing .NET 9 AOT..."
    cd "$ROOT_DIR/implementations/dotnet9aot" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        dotnet build
    else
        dotnet build >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result ".NET 9 AOT" $result
    return $result
}

# Test Go
test_go() {
    log_info "Testing Go..."
    cd "$ROOT_DIR/implementations/go" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        go build
    else
        go build >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Go 1.23+" $result
    return $result
}

# Test Rust
test_rust() {
    log_info "Testing Rust..."
    cd "$ROOT_DIR/implementations/rust" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        cargo check
    else
        cargo check >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Rust" $result
    return $result
}

# Test Python
test_python() {
    log_info "Testing Python..."
    cd "$ROOT_DIR/implementations/python" || return 1

    if [[ "$VERBOSE" == "true" ]]; then
        python3 -m py_compile main.py models.py
    else
        python3 -m py_compile main.py models.py 2>> "$LOG_FILE"
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Python 3" $result
    return $result
}

# Test Bun
test_bun() {
    log_info "Testing Bun/TypeScript..."
    cd "$ROOT_DIR/implementations/bun" || return 1

    # Install dependencies if needed
    if [[ ! -d "node_modules" ]]; then
        if [[ "$VERBOSE" == "true" ]]; then
            bun install
        else
            bun install >> "$LOG_FILE" 2>&1
        fi
    fi

    # Type check
    if [[ "$VERBOSE" == "true" ]]; then
        bunx tsc --noEmit
    else
        bunx tsc --noEmit >> "$LOG_FILE" 2>&1
    fi

    local result=$?
    cd "$SCRIPT_DIR"
    test_result "Bun/TypeScript" $result
    return $result
}

# Summary report
print_summary() {
    echo ""
    echo "═══════════════════════════════════════════════════════════"
    echo "Build Test Summary"
    echo "═══════════════════════════════════════════════════════════"
    echo ""

    local total=$((PASSED + FAILED))

    echo -e "Total Implementations: $total"
    echo -e "  ${GREEN}✓ Passed:${NC}  $PASSED"
    echo -e "  ${RED}✗ Failed:${NC}  $FAILED"
    echo ""

    if [[ $FAILED -eq 0 && $PASSED -gt 0 ]]; then
        echo -e "${GREEN}✓ All implementations build successfully!${NC}"
        echo ""
        echo "Your environment is ready. Next steps:"
        echo "  1. Run performance tests: ./run-all-tests.sh"
        return 0
    else
        echo -e "${RED}✗ Some implementations failed to build.${NC}"
        echo ""
        echo "Check the log file for details: $LOG_FILE"
        echo ""
        echo "Troubleshooting:"
        echo "  1. Run ./verify-environment.sh to check tool installations"
        echo "  2. Ensure all required tools are installed: ./setup-environment.sh"
        echo "  3. Check the log file for specific error messages"
        return 1
    fi
}

# Main test flow
main() {
    echo ""
    echo "╔════════════════════════════════════════════════════════════╗"
    echo "║  Reference Service Tech Stack Comparison (mise)            ║"
    echo "║  Build Test Suite                                          ║"
    echo "╚════════════════════════════════════════════════════════════╝"
    echo ""

    # Initialize log file
    mkdir -p "$(dirname "$LOG_FILE")"
    echo "Build testing started at $(date)" > "$LOG_FILE"
    log "Log file: $LOG_FILE"
    echo ""

    log "Testing all implementations..."
    echo ""
    echo "Build Results:"
    echo "-------------"

    # Test event libraries first (dependencies for Java implementations)
    build_java21_events
    build_java25_events

    echo ""

    # Test all Java 21 implementations
    test_java21_springboot
    test_java21_springboot_graal
    test_java21_quarkus

    # Test all Java 25 implementations
    test_java25_springboot
    test_java25_springboot_graal
    test_java25_quarkus

    # Test all non-Java implementations
    test_dotnet9
    test_dotnet9_aot
    test_go
    test_rust
    test_python
    test_bun

    # Print summary
    print_summary
}

# Run main function
main
exit $?
