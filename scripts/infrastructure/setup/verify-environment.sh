#!/usr/bin/env bash
# verify-environment.sh
# Verifies that all required tools and infrastructure are properly installed

set -euo pipefail

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

# Source shared library (for colors and common functions)
source "${ROOT_DIR}/scripts/common.sh"

# Activate mise for this script if mise is available
if command_exists mise; then
    # Initialize PROMPT_COMMAND to avoid "unbound variable" errors with set -u
    PROMPT_COMMAND="${PROMPT_COMMAND:-}"
    eval "$(mise activate bash)"
fi

# Status counters
PASSED=0
FAILED=0
WARNINGS=0

# Print functions
print_header() {
    echo -e "${BLUE}======================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}======================================${NC}"
}

print_subheader() {
    echo -e "\n${BLUE}--- $1 ---${NC}"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
    PASSED=$((PASSED + 1))
}

print_failure() {
    echo -e "${RED}✗${NC} $1"
    FAILED=$((FAILED + 1))
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
    WARNINGS=$((WARNINGS + 1))
}

print_info() {
    echo -e "  $1"
}

# Function to check if mise is installed
check_mise_installed() {
    print_subheader "Checking mise Installation"

    if command -v mise &> /dev/null; then
        MISE_VERSION=$(mise --version 2>/dev/null || echo "unknown")
        print_success "mise is installed: $MISE_VERSION"

        # Check if version is recent enough (2024.1.0+)
        MISE_YEAR=$(echo "$MISE_VERSION" | cut -d. -f1)
        if [[ "$MISE_YEAR" -ge 2024 ]]; then
            print_success "mise version is recent enough (>= 2024.1.0)"
        else
            print_warning "mise version may be outdated. Recommended: 2024.1.0+"
        fi

        # Check mise location
        MISE_PATH=$(which mise)
        print_info "Location: $MISE_PATH"
    else
        print_failure "mise is not installed"
        print_info "Install with: curl https://mise.run | sh"
        print_info "Or run: ./setup-environment.sh"
    fi
    return 0
}

# Function to check if mise is activated in the current shell
check_mise_activated() {
    print_subheader "Checking mise Activation"

    # Check if mise is in PATH and functioning
    if mise version &> /dev/null; then
        print_success "mise is activated in current shell"

        # Check if MISE_SHELL is set (more reliable indicator than MISE_DATA_DIR)
        if [[ -n "${MISE_SHELL:-}" ]]; then
            print_success "mise environment is properly initialized (MISE_SHELL=$MISE_SHELL)"
        elif [[ -n "${MISE_DATA_DIR:-}" ]]; then
            print_success "mise environment variables are set"
            print_info "MISE_DATA_DIR: $MISE_DATA_DIR"
        else
            print_warning "mise environment not fully initialized"
            local shell_type
            shell_type=$(detect_shell)
            if [[ "$shell_type" != "unknown" ]]; then
                print_info "Run: eval \"\$(mise activate $shell_type)\""
            else
                print_info "Run: eval \"\$(mise activate bash)\"  # or zsh"
            fi
        fi
    else
        print_failure "mise is not activated in current shell"
        print_info "Add to your shell config (~/.bashrc or ~/.zshrc):"
        print_info "  eval \"\$(mise activate bash)\"  # or zsh"
    fi
    return 0
}

# Function to check a specific tool version
check_tool_version() {
    local tool_name=$1
    local expected_version=$2
    local check_command=$3

    if command -v "$tool_name" &> /dev/null; then
        ACTUAL_VERSION=$($check_command 2>&1 || echo "unknown")
        print_success "$tool_name is installed: $ACTUAL_VERSION"
    else
        print_failure "$tool_name is not found in PATH"
    fi
    return 0
}

# Function to check all mise-managed tools
check_mise_tools() {
    print_subheader "Checking mise-managed Tools"

    # First, show what mise thinks is installed
    if command -v mise &> /dev/null; then
        print_info "Installed tools via mise:"
        local mise_output
        mise_output=$(mise list --current 2>/dev/null || true)
        if [[ -n "$mise_output" ]]; then
            while IFS= read -r line; do
                print_info "  $line"
            done <<< "$mise_output"
        fi
        echo ""
    fi

    # Check Java 21 (via GraalVM) - Test version switching
    print_info "Checking Java 21 (provided by GraalVM)..."
    if mise list java 2>/dev/null | grep -qE "(java\s+|graalvm-jdk-)21\."; then
        # Test that we can actually switch to and use Java 21
        if JAVA_21_VERSION=$(mise exec java@21 -- java -version 2>&1 | head -1); then
            print_success "Java 21 is available and functional via mise"
            print_info "Version: $JAVA_21_VERSION"

            # Test native-image for Java 21
            if mise exec java@21 -- native-image --version &> /dev/null; then
                NATIVE_IMAGE_21=$(mise exec java@21 -- native-image --version 2>&1 | head -1)
                print_success "native-image is available: $NATIVE_IMAGE_21"
            else
                print_warning "native-image may not be available in Java 21"
            fi

            # Test javac
            if mise exec java@21 -- javac -version &> /dev/null; then
                print_success "javac (Java compiler) is available"
            else
                print_warning "javac may not be available in Java 21"
            fi
        else
            print_failure "Java 21 is installed but cannot be executed"
            print_info "Try: mise use java@21 && java -version"
        fi
    else
        print_failure "Java 21 is not installed via mise"
        print_info "Install GraalVM 21: ./install-graalvm.sh"
    fi

    # Check Java 25 (via GraalVM) - Test version switching
    print_info "Checking Java 25 (provided by GraalVM)..."
    if mise list java 2>/dev/null | grep -qE "(java\s+|graalvm-jdk-)25\."; then
        # Test that we can actually switch to and use Java 25
        if JAVA_25_VERSION=$(mise exec java@25 -- java -version 2>&1 | head -1); then
            print_success "Java 25 is available and functional via mise"
            print_info "Version: $JAVA_25_VERSION"

            # Test native-image for Java 25
            if mise exec java@25 -- native-image --version &> /dev/null; then
                NATIVE_IMAGE_25=$(mise exec java@25 -- native-image --version 2>&1 | head -1)
                print_success "native-image is available: $NATIVE_IMAGE_25"
            else
                print_warning "native-image may not be available in Java 25"
            fi

            # Test javac
            if mise exec java@25 -- javac -version &> /dev/null; then
                print_success "javac (Java compiler) is available"
            else
                print_warning "javac may not be available in Java 25"
            fi
        else
            print_failure "Java 25 is installed but cannot be executed"
            print_info "Try: mise use java@25 && java -version"
        fi
    else
        print_failure "Java 25 is not installed via mise"
        print_info "Install GraalVM 25: ./install-graalvm.sh"
    fi

    # Test Java version switching capability
    print_info "Testing Java version switching..."
    local switch_test_passed=true

    # Test switching to Java 21
    if mise list java 2>/dev/null | grep -qE "(java\s+|graalvm-jdk-)21\."; then
        if mise exec java@21 -- java -version &> /dev/null; then
            print_success "Can switch to Java 21 (mise exec java@21 -- java -version)"
        else
            print_failure "Cannot switch to Java 21"
            switch_test_passed=false
        fi
    fi

    # Test switching to Java 25
    if mise list java 2>/dev/null | grep -qE "(java\s+|graalvm-jdk-)25\."; then
        if mise exec java@25 -- java -version &> /dev/null; then
            print_success "Can switch to Java 25 (mise exec java@25 -- java -version)"
        else
            print_failure "Cannot switch to Java 25"
            switch_test_passed=false
        fi
    fi

    if [[ "$switch_test_passed" == true ]]; then
        print_success "Java version switching is working correctly"
        print_info "Build scripts will use: mise exec java@21 -- <command>"
        print_info "                    or: mise exec java@25 -- <command>"
    else
        print_failure "Java version switching has issues"
        print_info "Verify mise is properly activated in your shell"
    fi

    # Check Maven
    print_info "Checking Maven..."
    if mise list maven 2>/dev/null | grep -q "maven"; then
        check_tool_version "mvn" "3.9.11" "mvn --version"
    else
        print_failure "Maven is not installed via mise"
        print_info "Install with: mise install maven@3.9.11"
    fi

    # Check Go
    print_info "Checking Go..."
    if mise list go 2>/dev/null | grep -q "go"; then
        check_tool_version "go" "1.23.12" "go version"
    else
        print_failure "Go is not installed via mise"
        print_info "Install with: mise install go@1.23.12"
    fi

    # Check Rust
    print_info "Checking Rust..."
    if mise list rust 2>/dev/null | grep -q "rust"; then
        # Test that we can actually use Rust via mise
        if RUST_VERSION=$(mise exec rust@latest -- rustc --version 2>&1); then
            print_success "rustc is available via mise: $RUST_VERSION"
        else
            print_failure "Rust is installed but cannot be executed"
        fi

        # Check cargo
        if mise exec rust@latest -- cargo --version &> /dev/null; then
            print_success "cargo is available"
        fi
    else
        print_failure "Rust is not installed via mise"
        print_info "Install with: mise install rust@stable"
    fi

    # Check Python
    print_info "Checking Python 3.13+..."
    if mise list python 2>/dev/null | grep -q "python"; then
        check_tool_version "python3" "3.13.8" "python3 --version"

        # Check pip via mise's Python
        if mise exec python@latest -- python -c "import pip" &> /dev/null; then
            print_success "pip is available"

            # Check for performance-tester required packages
            print_info "Checking performance-tester Python packages..."
            for pkg in matplotlib pika psutil; do
                if mise exec python@latest -- python -c "import $pkg" &> /dev/null; then
                    print_success "$pkg is installed"
                else
                    print_warning "$pkg is not installed"
                    print_info "Install with: python -m pip install -r performance-tester/requirements.txt"
                fi
            done

            # Check for Python service required packages
            print_info "Checking Python service packages..."
            # Use specific import names (fastapi, uvicorn, psycopg, sqlalchemy)
            if mise exec python@latest -- python -c "import fastapi" &> /dev/null; then
                print_success "fastapi is installed"
            else
                print_warning "fastapi is not installed"
                print_info "Install with: python -m pip install -r implementations/python/requirements.txt"
            fi

            if mise exec python@latest -- python -c "import uvicorn" &> /dev/null; then
                print_success "uvicorn is installed"
            else
                print_warning "uvicorn is not installed"
                print_info "Install with: python -m pip install -r implementations/python/requirements.txt"
            fi

            if mise exec python@latest -- python -c "import psycopg" &> /dev/null; then
                print_success "psycopg is installed"
            else
                print_warning "psycopg is not installed"
                print_info "Install with: python -m pip install -r implementations/python/requirements.txt"
            fi

            if mise exec python@latest -- python -c "import sqlalchemy" &> /dev/null; then
                print_success "sqlalchemy is installed"
            else
                print_warning "sqlalchemy is not installed"
                print_info "Install with: python -m pip install -r implementations/python/requirements.txt"
            fi
        else
            print_warning "pip is not available"
        fi
    else
        print_failure "Python is not installed via mise"
        print_info "Install with: mise install python@3.13.8"
    fi

    # Check Bun
    print_info "Checking Bun..."
    if mise list bun 2>/dev/null | grep -q "bun"; then
        check_tool_version "bun" "1.3.1" "bun --version"
    else
        print_failure "Bun is not installed via mise"
        print_info "Install with: mise install bun@1.3.1"
    fi

    # Check .NET (managed by mise)
    print_info "Checking .NET 9..."
    if mise list dotnet 2>/dev/null | grep -q "dotnet"; then
        check_tool_version "dotnet" "9.0" "dotnet --version"
    elif command -v dotnet &> /dev/null; then
        local dotnet_version
        dotnet_version=$(dotnet --version 2>/dev/null)
        if [[ "$dotnet_version" == 9.* ]]; then
            print_success "dotnet is installed: $dotnet_version (system)"
        else
            print_warning "dotnet version $dotnet_version found, expected 9.x"
        fi
    else
        print_failure ".NET is not installed"
        print_info "Install with: mise install dotnet@9.0.306"
    fi
}

# Function to test mise runtime management
check_mise_runtime_management() {
    print_subheader "Testing mise Runtime Management"

    # Test Java 21 runtime via mise
    print_info "Testing Java 21 runtime management..."
    if mise list java 2>/dev/null | grep -qE "(java\s+|graalvm-jdk-)21\."; then
        # Test that mise can set JAVA_HOME and run Java 21
        local java21_home
        java21_home=$(mise exec java@21 -- bash -c 'echo $JAVA_HOME' 2>/dev/null)
        if [[ -n "$java21_home" ]] && [[ -d "$java21_home" ]]; then
            print_success "mise correctly manages JAVA_HOME for Java 21: $java21_home"
        else
            print_failure "mise cannot set JAVA_HOME for Java 21"
        fi

        # Test that native-image works
        if mise exec java@21 -- bash -c 'which native-image' &> /dev/null; then
            print_success "mise provides native-image for Java 21"
        else
            print_failure "mise cannot provide native-image for Java 21"
        fi
    fi

    # Test Java 25 runtime via mise
    print_info "Testing Java 25 runtime management..."
    if mise list java 2>/dev/null | grep -qE "(java\s+|graalvm-jdk-)25\."; then
        # Test that mise can set JAVA_HOME and run Java 25
        local java25_home
        java25_home=$(mise exec java@25 -- bash -c 'echo $JAVA_HOME' 2>/dev/null)
        if [[ -n "$java25_home" ]] && [[ -d "$java25_home" ]]; then
            print_success "mise correctly manages JAVA_HOME for Java 25: $java25_home"
        else
            print_failure "mise cannot set JAVA_HOME for Java 25"
        fi

        # Test that native-image works
        if mise exec java@25 -- bash -c 'which native-image' &> /dev/null; then
            print_success "mise provides native-image for Java 25"
        else
            print_failure "mise cannot provide native-image for Java 25"
        fi
    fi

    # Test Go runtime management
    print_info "Testing Go runtime management..."
    if mise list go 2>/dev/null | grep -q "go"; then
        local gopath
        gopath=$(mise exec go@latest -- bash -c 'echo $GOPATH' 2>/dev/null)
        if [[ -n "$gopath" ]]; then
            print_success "mise correctly manages GOPATH: $gopath"
        else
            print_info "GOPATH not set by mise (using Go default: ~/go)"
        fi
    fi

    # Test .NET runtime management
    print_info "Testing .NET runtime management..."
    if command -v dotnet &> /dev/null; then
        local dotnet_root="${DOTNET_ROOT:-}"
        if [[ -n "$dotnet_root" ]] && [[ -d "$dotnet_root" ]]; then
            if mise list dotnet 2>/dev/null | grep -q "dotnet"; then
                print_success "DOTNET_ROOT: $dotnet_root (mise)"
            else
                print_success "DOTNET_ROOT: $dotnet_root (system)"
            fi
        else
            print_info "DOTNET_ROOT not explicitly set (using default)"
        fi
    fi

    # Test Rust runtime management
    print_info "Testing Rust runtime management..."
    if mise list rust 2>/dev/null | grep -q "rust"; then
        local cargo_home
        cargo_home=$(mise exec rust@latest -- bash -c 'echo $CARGO_HOME' 2>/dev/null)
        if [[ -n "$cargo_home" ]] && [[ -d "$cargo_home" ]]; then
            print_success "mise correctly manages CARGO_HOME: $cargo_home"
        else
            print_info "CARGO_HOME not explicitly set (using Rust default: ~/.cargo)"
        fi
    fi

    # Test Python runtime management
    print_info "Testing Python runtime management..."
    if mise list python 2>/dev/null | grep -q "python"; then
        # Test that mise provides Python with packages
        if mise exec python@latest -- python -c "import sys; print(sys.executable)" &> /dev/null; then
            local python_path
            python_path=$(mise exec python@latest -- python -c "import sys; print(sys.executable)" 2>/dev/null)
            print_success "mise correctly manages Python runtime: $python_path"
        else
            print_failure "mise cannot provide Python runtime"
        fi
    fi
}

# Function to check system packages (not managed by mise)
check_system_packages() {
    print_subheader "Checking System Packages"

    # Check k6 (must be installed separately)
    print_info "Checking k6..."
    if command -v k6 &> /dev/null; then
        K6_VERSION=$(k6 version 2>&1 | head -1 || echo "unknown")
        print_success "k6 is installed: $K6_VERSION"
    else
        print_failure "k6 is not installed"
        print_info "Install with: sudo snap install k6"
        print_info "Or see: https://k6.io/docs/getting-started/installation/"
    fi

    # Check Docker
    print_info "Checking Docker..."
    if command -v docker &> /dev/null; then
        DOCKER_VERSION=$(docker --version 2>&1 || echo "unknown")
        print_success "Docker is installed: $DOCKER_VERSION"

        # Check if Docker daemon is running
        if docker ps &> /dev/null; then
            print_success "Docker daemon is running"
        else
            print_warning "Docker daemon is not running or not accessible"
            print_info "Start with: sudo systemctl start docker"
        fi
    else
        print_failure "Docker is not installed"
        print_info "Install from: https://docs.docker.com/engine/install/"
    fi

    # Check Docker Compose
    print_info "Checking Docker Compose..."
    if command -v docker-compose &> /dev/null; then
        COMPOSE_VERSION=$(docker-compose --version 2>&1 || echo "unknown")
        print_success "docker-compose is installed: $COMPOSE_VERSION"
    elif docker compose version &> /dev/null; then
        COMPOSE_VERSION=$(docker compose version 2>&1 || echo "unknown")
        print_success "docker compose (plugin) is installed: $COMPOSE_VERSION"
    else
        print_failure "Docker Compose is not installed"
        print_info "Install from: https://docs.docker.com/compose/install/"
    fi
}

# Function to check Docker infrastructure
check_docker_infrastructure() {
    print_subheader "Checking Docker Infrastructure"

    # Check if Docker is available
    if ! command -v docker &> /dev/null; then
        print_warning "Docker is not available - skipping infrastructure checks"
        return 0
    fi

    # Check if Docker daemon is running
    if ! docker ps &> /dev/null; then
        print_warning "Docker daemon is not running - skipping infrastructure checks"
        print_info "Start with: sudo systemctl start docker"
        return 0
    fi
}

# Function to print summary
print_summary() {
    echo ""
    print_header "Verification Summary"

    echo -e "${GREEN}Passed:${NC} $PASSED"
    echo -e "${RED}Failed:${NC} $FAILED"
    echo -e "${YELLOW}Warnings:${NC} $WARNINGS"

    echo ""

    if [[ $FAILED -eq 0 ]]; then
        echo -e "${GREEN}✓ All critical checks passed!${NC}"
        echo ""
        echo "Next steps:"
        echo "  1. Build all implementations: ./test-all-builds.sh"
        echo "  2. Run performance tests: ./run-all-tests.sh --events 1000 --duration 10s"
        echo "  3. Quick test: ./run-all-tests.sh --events 100 --duration 5s"
        echo ""
        if [[ $WARNINGS -gt 0 ]]; then
            echo -e "${YELLOW}Note:${NC} Some warnings were found. Review them above."
        fi
        return 0
    else
        echo -e "${RED}✗ Some checks failed.${NC}"
        echo ""
        echo "Remediation steps:"
        echo "  1. Install missing tools: ./setup-environment.sh"
        local shell_type
        shell_type=$(detect_shell)
        if [[ "$shell_type" != "unknown" ]]; then
            echo "  2. Activate mise: eval \"\$(mise activate $shell_type)\""
        else
            echo "  2. Activate mise: eval \"\$(mise activate bash)\"  # or zsh"
        fi
        echo "  3. Install all tools: mise install"
        echo "  4. Re-run this script: ./verify-environment.sh"
        echo ""
        return 1
    fi
}

# Main execution
main() {
    print_header "mise Environment Verification"
    echo "This script checks that all required tools are properly installed via mise."
    echo ""

    # Run all checks
    check_mise_installed
    check_mise_activated
    check_mise_tools
    check_mise_runtime_management
    check_system_packages
    check_docker_infrastructure

    # Print summary
    print_summary
}

# Run main function
main
