#!/bin/bash

# Master Test Automation Script
# This script automates testing for all services in the performance test project
# It: drops DBs, creates DBs, runs each app, tests it, then kills it

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

# Source shared library
source "${ROOT_DIR}/scripts/common.sh"

POSTGRES_CONTAINER="performancetest-postgres"
DB_USER="admin"
DB_PASS="admin"
RABBITMQ_CONTAINER="performancetest-rabbitmq"
RABBITMQ_USER="admin"
RABBITMQ_VHOST="/"
# POSTGRES_HOST and RABBITMQ_HOST are set by detect_container_environment() after sourcing common.sh

# Default test configuration
NUM_EVENTS=10000
API_DURATION="30s"
API_WORKERS=1
GRAAL_MODE="jar"  # Default to jar for quick testing; use --native for native executables
MAX_SERVICE_STARTUP_WAIT=90  # Maximum time (in seconds) to wait for a service to become available
RESULTS_FOLDER=""  # Set after argument parsing based on tester type

DOTNET_TESTER_DIR="$ROOT_DIR/performance-tester-dotnet/src/PerformanceTester.Cli"

# Queue names for each service (used for unbinding after tests)
# These must match the queue names declared by each service implementation
QUEUE_NAME_BUN="bun"
QUEUE_NAME_DOTNET9="dotnet9"
QUEUE_NAME_DOTNET9AOT="dotnet9aot"
QUEUE_NAME_GO="go"
QUEUE_NAME_PYTHON="pythonFastAPI"
QUEUE_NAME_RUST="rustActix"
QUEUE_NAME_JAVA21_SPRINGBOOT="javaSB"
QUEUE_NAME_JAVA21_SPRINGBOOT_GRAAL="javaSBGraal"
QUEUE_NAME_JAVA21_QUARKUS_GRAAL="javaQuarkusGraal"
QUEUE_NAME_JAVA25_SPRINGBOOT="javaSB"
QUEUE_NAME_JAVA25_SPRINGBOOT_GRAAL="javaSB25Graal"
QUEUE_NAME_JAVA25_QUARKUS_GRAAL="javaQuarkusGraal25"
RABBITMQ_EXCHANGE="referenceservice.comparison"
RABBITMQ_ROUTING_KEY="instrument.status.changed"

# Java version management (mise)
# Detect available Java versions for Java 21 and Java 25 projects
JAVA_21_VERSION=""
JAVA_25_VERSION=""

# Activate mise using shared library function
if ! activate_mise; then
    log_error "Failed to activate mise"
    exit 1
fi

# Detect Java versions using shared library function
detect_java_versions

# Detect container environment and set POSTGRES_HOST/RABBITMQ_HOST
# This must be called early, before any functions that use these variables
if ! detect_container_environment; then
    log_error "Failed to detect container environment"
    exit 1
fi

# Show usage
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Master test automation script for performance test services.

OPTIONS:
    -e, --events NUM        Number of events to publish/consume (default: 10000)
    -d, --duration TIME     API load test duration (e.g., 5s, 1m, 2h) (default: 30s)
    -w, --workers NUM       Number of concurrent API workers (default: 1)
    -n, --native            Build GraalVM services as native executables (default: jar mode)
                            Note: Native builds take ~10 minutes but provide better benchmarks
    -r, --results-folder    Folder to save test results (default: ./performance-tester-dotnet/test-results)
    -h, --help              Show this help message

EXAMPLES:
    # Run with defaults (10000 events, 30s API test, 1 worker, JAR mode for GraalVM)
    $0

    # Quick test with 100 events and 5 second API test
    $0 --events 100 --duration 5s

    # Heavy load test with 50000 events, 2 minute API test, 4 workers
    $0 -e 50000 -d 2m -w 4

    # Full native build for accurate GraalVM benchmarks (takes longer)
    $0 --native

SERVICES TESTED:
    1. Bun (port 8090)
    2. Python (port 8099)
    3. Go (port 8094)
    4. Rust (port 8100)
    5. .NET 9 (port 8092)
    6. .NET 9 AOT (port 8093)
    7. Java 21 Spring Boot (port 8097)
    8. Java 21 Spring Boot GraalVM (port 8098)
    9. Java 21 Quarkus GraalVM (port 8096)
   10. Java 25 Spring Boot (port 8101)
   11. Java 25 Spring Boot GraalVM (port 8102)
   12. Java 25 Quarkus GraalVM (port 8103) [Experimental]

EOF
}

# Parse command-line arguments
parse_args() {
    while [[ $# -gt 0 ]]; do
        case $1 in
            -e|--events)
                NUM_EVENTS="$2"
                if ! [[ "$NUM_EVENTS" =~ ^[0-9]+$ ]]; then
                    log_error "Events must be a positive number"
                    exit 1
                fi
                shift 2
                ;;
            -d|--duration)
                API_DURATION="$2"
                if ! [[ "$API_DURATION" =~ ^[0-9]+[smh]$ ]]; then
                    log_error "Duration must be in format: NUMBER[s|m|h] (e.g., 30s, 5m, 2h)"
                    exit 1
                fi
                shift 2
                ;;
            -w|--workers)
                API_WORKERS="$2"
                if ! [[ "$API_WORKERS" =~ ^[0-9]+$ ]]; then
                    log_error "Workers must be a positive number"
                    exit 1
                fi
                shift 2
                ;;
            -n|--native)
                GRAAL_MODE="native"
                shift
                ;;
            -r|--results-folder)
                RESULTS_FOLDER="$2"
                if [[ -z "$RESULTS_FOLDER" ]]; then
                    log_error "Results folder cannot be empty"
                    exit 1
                fi
                shift 2
                ;;
            -h|--help)
                show_usage
                exit 0
                ;;
            *)
                log_error "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
}

# Local logging functions (simpler format without timestamps)
# Note: These override the shared library functions for this script's specific needs
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_section() {
    echo ""
    echo -e "${BLUE}========================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}========================================${NC}"
}

# Function to check if Docker containers are running
check_docker() {
    log_section "Checking Docker Infrastructure"

    cd "$SCRIPT_DIR"

    # Check if our containers already exist
    local postgres_running=false
    local rabbitmq_running=false

    if docker ps --filter "name=performancetest-postgres" --filter "status=running" --format "{{.Names}}" | grep -q "performancetest-postgres"; then
        postgres_running=true
        log_info "PostgreSQL container already running"
    fi

    if docker ps --filter "name=performancetest-rabbitmq" --filter "status=running" --format "{{.Names}}" | grep -q "performancetest-rabbitmq"; then
        rabbitmq_running=true
        log_info "RabbitMQ container already running"
    fi

    # If both are running, we're good
    if [ "$postgres_running" = true ] && [ "$rabbitmq_running" = true ]; then
        log_success "Docker infrastructure is ready"
        return 0
    fi

    # Otherwise, clean up any existing containers (even if from different project) and recreate
    log_info "Setting up Docker infrastructure..."

    # Remove containers if they exist (regardless of state or project name)
    if docker ps -a --filter "name=performancetest-postgres" --format "{{.Names}}" | grep -q "performancetest-postgres"; then
        log_info "Removing existing performancetest-postgres container..."
        docker rm -f performancetest-postgres 2>/dev/null || true
    fi

    if docker ps -a --filter "name=performancetest-rabbitmq" --format "{{.Names}}" | grep -q "performancetest-rabbitmq"; then
        log_info "Removing existing performancetest-rabbitmq container..."
        docker rm -f performancetest-rabbitmq 2>/dev/null || true
    fi

    log_info "Starting Docker containers..."
    docker-compose -f "$ROOT_DIR/scripts/infrastructure/docker-compose.yml" up -d
    sleep 5
}

# Function to wait for PostgreSQL to be ready
wait_for_postgres() {
    local max_wait=${1:-$MAX_SERVICE_STARTUP_WAIT}
    local waited=0

    log_info "Waiting for PostgreSQL to be ready (max ${max_wait}s)"

    while [ $waited -lt $max_wait ]; do
        # Try to connect to postgres database - this will succeed only when PostgreSQL is fully ready
        if docker exec "$POSTGRES_CONTAINER" psql -U "$DB_USER" -d postgres -c "SELECT 1" > /dev/null 2>&1; then
            log_success "PostgreSQL is ready (after ${waited}s)"
            return 0
        fi
        sleep 1
        waited=$((waited + 1))
    done

    log_error "PostgreSQL did not become ready within ${max_wait}s"
    return 1
}

# Function to ensure all databases exist (idempotent, safe to run repeatedly)
ensure_databases() {
    log_section "Ensuring All Databases Exist"

    # Wait for PostgreSQL to be fully ready before attempting database operations
    if ! wait_for_postgres; then
        log_error "Cannot create databases - PostgreSQL not ready"
        exit 1
    fi

    if [ -f "$ROOT_DIR/scripts/infrastructure/create-databases.sql" ]; then
        log_info "Running create-databases.sql (creates databases if they don't exist)"
        docker exec -i "$POSTGRES_CONTAINER" psql -U "$DB_USER" -d postgres < "$ROOT_DIR/scripts/infrastructure/create-databases.sql" > /dev/null
        log_success "All databases are ready"
    else
        log_error "create-databases.sql not found!"
        exit 1
    fi
}

# Function to wait for RabbitMQ to be ready
wait_for_rabbitmq() {
    local max_wait=${1:-$MAX_SERVICE_STARTUP_WAIT}
    local waited=0

    log_info "Waiting for RabbitMQ to be ready (max ${max_wait}s)"

    while [ $waited -lt $max_wait ]; do
        # Try to list vhosts - this will succeed only when RabbitMQ is fully ready
        if docker exec "$RABBITMQ_CONTAINER" rabbitmqctl list_vhosts > /dev/null 2>&1; then
            log_success "RabbitMQ is ready (after ${waited}s)"
            return 0
        fi
        sleep 1
        waited=$((waited + 1))
    done

    log_error "RabbitMQ did not become ready within ${max_wait}s"
    return 1
}

# Function to clear RabbitMQ queues
clear_rabbitmq() {
    log_section "Clearing RabbitMQ Queues"

    # Wait for RabbitMQ to be fully ready before attempting operations
    if ! wait_for_rabbitmq; then
        log_error "Cannot clear RabbitMQ queues - service not ready"
        exit 1
    fi

    log_info "Resetting RabbitMQ vhost to clear all queues/exchanges"
    docker exec "$RABBITMQ_CONTAINER" rabbitmqctl delete_vhost "$RABBITMQ_VHOST" 2>/dev/null || true
    docker exec "$RABBITMQ_CONTAINER" rabbitmqctl add_vhost "$RABBITMQ_VHOST"
    docker exec "$RABBITMQ_CONTAINER" rabbitmqctl set_permissions -p "$RABBITMQ_VHOST" "$RABBITMQ_USER" ".*" ".*" ".*"

    log_success "RabbitMQ vhost reset complete"
}

# Function to kill a process by PID
kill_process() {
    local pid=$1
    if [ -n "$pid" ] && ps -p "$pid" > /dev/null 2>&1; then
        log_info "Killing process $pid"
        kill "$pid" 2>/dev/null || true
        sleep 2
        # Force kill if still running
        if ps -p "$pid" > /dev/null 2>&1; then
            log_warn "Force killing process $pid"
            kill -9 "$pid" 2>/dev/null || true
        fi
    fi
}

# Function to kill process by port
kill_by_port() {
    local port=$1
    log_info "Checking for processes on port $port"
    # Try lsof first
    local pids=$(lsof -ti ":$port" 2>/dev/null || true)
    # If lsof didn't find anything, try ss
    if [ -z "$pids" ]; then
        pids=$(ss -tlnp 2>/dev/null | grep ":$port " | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | sort -u || true)
    fi
    if [ -n "$pids" ]; then
        for pid in $pids; do
            kill_process "$pid"
        done
    fi
}

# Java version helpers (mise)
# Note: We use 'mise exec java@VERSION -- command' to run commands with specific Java versions
# This approach does not modify .mise.toml and works correctly in non-interactive scripts

# Function to check if a port is listening
# Uses ss (preferred) or lsof as fallback
is_port_listening() {
    local port=$1
    # Try ss first (more reliable in containers)
    if ss -tlnp 2>/dev/null | grep -q ":$port "; then
        return 0
    fi
    # Fall back to lsof
    if lsof -ti ":$port" > /dev/null 2>&1; then
        return 0
    fi
    return 1
}

# Function to wait for service to be ready
wait_for_service() {
    local port=$1
    local max_wait=$2
    local waited=0

    log_info "Waiting for service on port $port (max ${max_wait}s)"

    # Phase 1: Wait for port to be listening
    log_info "Phase 1: Checking if port $port is listening..."
    while [ $waited -lt $max_wait ]; do
        if is_port_listening "$port"; then
            log_success "Port $port is listening (after ${waited}s)"
            break
        fi
        sleep 1
        waited=$((waited + 1))
    done

    if [ $waited -ge $max_wait ]; then
        log_error "Port $port did not open within ${max_wait}s"
        return 1
    fi

    # Phase 2: Wait for HTTP health check
    log_info "Phase 2: Checking if service responds to HTTP requests..."
    local health_check_start=$waited
    while [ $waited -lt $max_wait ]; do
        # Accept any HTTP response (including 404) as proof the service is running
        # The /kpi endpoint returns 404 when database is empty, which is expected at startup
        if curl -s -o /dev/null -w "%{http_code}" "http://localhost:$port/kpi" 2>/dev/null | grep -q "[0-9][0-9][0-9]"; then
            log_success "Service is healthy and responding on port $port (after ${waited}s total)"
            return 0
        fi
        sleep 1
        waited=$((waited + 1))
    done

    log_error "Service did not pass health check within ${max_wait}s"
    log_error "Port opened after ${health_check_start}s, but HTTP endpoint never responded"
    return 1
}

# Function to run tests for a service
run_test() {
    local service_name=$1
    local port=$2
    local start_command=$3
    local app_dir=$4
    local max_startup_wait=${5:-30}

    log_section "Testing: $service_name (Port: $port)"

    # Kill any existing process on the port
    kill_by_port "$port"

    # Start the service
    log_info "Starting $service_name..."
    log_info "Command: $start_command"
    log_info "Directory: $app_dir"

    cd "$app_dir"

    # Export environment variables for service configuration
    # These will be used by all services to connect to PostgreSQL and RabbitMQ
    export POSTGRES_HOST="$POSTGRES_HOST"
    export RABBITMQ_HOST="$RABBITMQ_HOST"

    # Database URLs for services that need connection strings
    # Each service has its own database (lowercase to match create-databases.sql)
    local db_name="${service_name%ReferenceService}_db"
    db_name="${db_name,,}"  # Convert to lowercase
    export DATABASE_URL="postgresql://admin:admin@${POSTGRES_HOST}:5432/${db_name}?schema=public"

    # Individual database connection parameters
    export DB_HOST="$POSTGRES_HOST"
    export DB_PORT="5432"
    export DB_USER="admin"
    export DB_PASSWORD="admin"
    export DB_NAME="$db_name"

    # RabbitMQ connection parameters (used by Python test script and services)
    export RABBITMQ_USER="admin"
    export RABBITMQ_PASSWORD="admin"
    export RABBITMQ_PORT="5672"
    # Full RabbitMQ URL (used by Rust service)
    export RABBITMQ_URL="amqp://admin:admin@${RABBITMQ_HOST}:5672"

    # .NET-specific configuration overrides (uses double underscore for nested config)
    # Connection string (used by some .NET services)
    export ConnectionStrings__DefaultConnection="Host=${POSTGRES_HOST};Port=5432;Database=${db_name};Username=admin;Password=admin"
    # Individual Postgres settings (used by dotnet9 which builds its own connection string)
    export Postgres__Host="$POSTGRES_HOST"
    export Postgres__Port="5432"
    export Postgres__Database="$db_name"
    export Postgres__Username="admin"
    export Postgres__Password="admin"
    # RabbitMQ settings
    export RabbitMQ__Host="$RABBITMQ_HOST"
    export RabbitMQ__Port="$RABBITMQ_PORT"
    export RabbitMQ__User="$RABBITMQ_USER"
    export RabbitMQ__Password="$RABBITMQ_PASSWORD"

    eval "$start_command" > "/tmp/performancetest-${service_name}.log" 2>&1 &
    local app_pid=$!

    log_info "Service started with PID: $app_pid"

    # Wait for service to be ready
    if ! wait_for_service "$port" "$max_startup_wait"; then
        log_error "Failed to start $service_name"
        kill_process "$app_pid"
        cat "/tmp/performancetest-${service_name}.log" || true
        return 1
    fi

    # Run the tests using .NET performance tester
    log_info "Running performance tests..."

    local test_result=0
    if dotnet run --project "$DOTNET_TESTER_DIR" -- test \
        --port "$port" \
        --events "$NUM_EVENTS" \
        --api-duration "$API_DURATION" \
        --api-workers "$API_WORKERS" \
        --results-folder "$RESULTS_FOLDER" \
        --database "$db_name"; then
        log_success "Tests completed for $service_name"
    else
        log_error "Tests failed for $service_name"
        test_result=1
    fi

    if [ $test_result -ne 0 ]; then
        kill_process "$app_pid"
        return 1
    fi

    # Kill the service
    log_info "Stopping $service_name..."
    kill_process "$app_pid"

    # Additional cleanup by port (in case process spawned children)
    kill_by_port "$port"

    log_success "Completed testing $service_name"
    echo ""
}

# Main execution
main() {
    # Parse command-line arguments
    parse_args "$@"

    # Set default results folder if not explicitly provided
    if [[ -z "$RESULTS_FOLDER" ]]; then
        RESULTS_FOLDER="$ROOT_DIR/performance-tester-dotnet/test-results"
    fi

    log_section "Performance Test - Automated Test Suite"
    log_info "Test Configuration:"
    log_info "  Events: $NUM_EVENTS"
    log_info "  API Duration: $API_DURATION"
    log_info "  API Workers: $API_WORKERS"
    log_info "  GraalVM Mode: $GRAAL_MODE"
    log_info "  Results Folder: $RESULTS_FOLDER"
    echo ""

    # Display detected Java versions
    log_info "Java Environment:"
    if [ -n "$JAVA_21_VERSION" ]; then
        log_info "  Java 21: $JAVA_21_VERSION ✓"
    else
        log_error "  Java 21: NOT FOUND - Java 21 services will fail!"
    fi
    if [ -n "$JAVA_25_VERSION" ]; then
        log_info "  Java 25: $JAVA_25_VERSION ✓"
    else
        log_error "  Java 25: NOT FOUND - Java 25 services will fail!"
    fi
    echo ""

    # Check Docker infrastructure
    check_docker

    # Ensure databases exist (idempotent - doesn't drop existing databases)
    ensure_databases

    # Clear RabbitMQ queues
    clear_rabbitmq

    # Test each service
    # Note: All compiled languages are built in release mode before testing
    # Services are tested in alphabetical order by implementation name

    # Build Bun standalone executable
    log_section "Building Bun Standalone Executable"
    if [ -d "$ROOT_DIR/implementations/bun" ]; then
        cd "$ROOT_DIR/implementations/bun"
        log_info "Installing Bun dependencies..."
        bun install
        log_info "Generating Prisma client..."
        bunx prisma generate
        log_info "Building Bun standalone executable..."
        bun run build
        log_success "Bun executable built successfully"
    fi

    # 1. Bun (compiled standalone executable)
    run_test "bunReferenceService" 8090 \
        "./bunReferenceService" \
        "$ROOT_DIR/implementations/bun" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_BUN" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build .NET 9 binary
    log_section "Building .NET 9 Binary"
    if [ -d "$ROOT_DIR/implementations/dotnet9" ]; then
        cd "$ROOT_DIR/implementations/dotnet9"
        log_info "Building .NET 9 release binary..."
        dotnet build -c Release -v quiet
        log_success ".NET 9 binary built successfully"
    fi

    # 2. .NET 9 (fast startup with pre-built binary)
    run_test "dotnet9ReferenceService" 8092 \
        "dotnet bin/Release/net9.0/dotnet9ReferenceService.dll" \
        "$ROOT_DIR/implementations/dotnet9" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_DOTNET9" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build .NET 9 AOT native binary
    log_section "Building .NET 9 AOT Native Binary"
    if [ -d "$ROOT_DIR/implementations/dotnet9aot" ]; then
        cd "$ROOT_DIR/implementations/dotnet9aot"
        log_info "Building .NET 9 AOT native binary..."
        dotnet publish -c Release -v quiet
        log_success ".NET 9 AOT native binary built successfully"
    fi

    # 3. .NET 9 AOT (fast startup with pre-built native binary)
    run_test "dotnet9AotReferenceService" 8093 \
        "./bin/Release/net9.0/linux-x64/publish/dotnet9AotReferenceService" \
        "$ROOT_DIR/implementations/dotnet9aot" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_DOTNET9AOT" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build Go binary
    log_section "Building Go Binary"
    if [ -d "$ROOT_DIR/implementations/go" ]; then
        cd "$ROOT_DIR/implementations/go"
        log_info "Building Go binary..."
        go build -o goReferenceService
        log_success "Go binary built successfully"
    fi

    # 4. Go (fast startup with pre-built binary)
    run_test "goReferenceService" 8094 \
        "./goReferenceService" \
        "$ROOT_DIR/implementations/go" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_GO" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build shared Java 21 library (needed by all Java 21 services)
    log_section "Building Shared Java 21 Library"
    if [ -d "$ROOT_DIR/implementations/java21.events" ]; then
        if [ -z "$JAVA_21_VERSION" ]; then
            log_error "Java 21 not found in mise! Please install it first:"
            log_error "  Run: ./setup-environment.sh"
            log_error "  Or manually: mise install java@21"
            exit 1
        fi
        log_info "Using Java 21 ($JAVA_21_VERSION)"
        cd "$ROOT_DIR/implementations/java21.events"
        log_info "Building java21.events library..."
        mise exec "java@$JAVA_21_VERSION" -- mvn clean install -q
        log_success "Java 21 library built successfully"
    fi

    # Build Java 21 Quarkus GraalVM
    if [ "$GRAAL_MODE" = "native" ]; then
        log_section "Building Java 21 Quarkus GraalVM Native Binary"
        if [ -d "$ROOT_DIR/implementations/java21quarkusgraal" ]; then
            log_info "Using Java 21 ($JAVA_21_VERSION)"
            cd "$ROOT_DIR/implementations/java21quarkusgraal"
            log_info "Building Java 21 Quarkus GraalVM native binary (this will take ~5 minutes)..."
            mise exec "java@$JAVA_21_VERSION" -- mvn package -Pnative -q -DskipTests
            log_success "Java 21 Quarkus GraalVM native binary built successfully"
        fi

        # 5. Java 21 Quarkus GraalVM (fast startup with native executable)
        run_test "java21QuarkusReferenceService" 8096 \
            "./target/java21QuarkusReferenceService-runner" \
            "$ROOT_DIR/implementations/java21quarkusgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA21_QUARKUS_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    else
        log_section "Building Java 21 Quarkus GraalVM JAR"
        if [ -d "$ROOT_DIR/implementations/java21quarkusgraal" ]; then
            log_info "Using Java 21 ($JAVA_21_VERSION)"
            cd "$ROOT_DIR/implementations/java21quarkusgraal"
            log_info "Building Java 21 Quarkus GraalVM JAR (quick mode)..."
            mise exec "java@$JAVA_21_VERSION" -- mvn clean package -q -DskipTests
            log_success "Java 21 Quarkus GraalVM JAR built successfully"
        fi

        # 5. Java 21 Quarkus GraalVM (running on JVM for quick testing)
        run_test "java21QuarkusReferenceService" 8096 \
            "mise exec java@$JAVA_21_VERSION -- java -jar target/java21QuarkusReferenceService.jar" \
            "$ROOT_DIR/implementations/java21quarkusgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA21_QUARKUS_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    fi

    # Build Java 21 Spring Boot JAR
    log_section "Building Java 21 Spring Boot JAR"
    if [ -d "$ROOT_DIR/implementations/java21springboot" ]; then
        log_info "Using Java 21 ($JAVA_21_VERSION)"
        cd "$ROOT_DIR/implementations/java21springboot"
        log_info "Building Java 21 Spring Boot JAR..."
        mise exec "java@$JAVA_21_VERSION" -- mvn clean package -q -DskipTests
        log_success "Java 21 Spring Boot JAR built successfully"
    fi

    # 6. Java 21 Spring Boot (fast startup with pre-built JAR)
    run_test "java21SpringBootReferenceService" 8097 \
        "mise exec java@$JAVA_21_VERSION -- java -jar target/java21SpringBootReferenceService.jar" \
        "$ROOT_DIR/implementations/java21springboot" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_JAVA21_SPRINGBOOT" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build Java 21 Spring Boot GraalVM
    if [ "$GRAAL_MODE" = "native" ]; then
        log_section "Building Java 21 Spring Boot GraalVM Native Binary"
        if [ -d "$ROOT_DIR/implementations/java21springbootgraal" ]; then
            log_info "Using Java 21 ($JAVA_21_VERSION)"
            cd "$ROOT_DIR/implementations/java21springbootgraal"
            log_info "Building Java 21 Spring Boot GraalVM native binary (this will take ~5 minutes)..."
            mise exec "java@$JAVA_21_VERSION" -- mvn clean package -Pnative -q -DskipTests
            log_success "Java 21 Spring Boot GraalVM native binary built successfully"
        fi

        # 7. Java 21 Spring Boot GraalVM (fast startup with native executable)
        run_test "java21SpringBootGraalReferenceService" 8098 \
            "./target/java21SpringBootGraalReferenceService" \
            "$ROOT_DIR/implementations/java21springbootgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA21_SPRINGBOOT_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    else
        log_section "Building Java 21 Spring Boot GraalVM JAR"
        if [ -d "$ROOT_DIR/implementations/java21springbootgraal" ]; then
            log_info "Using Java 21 ($JAVA_21_VERSION)"
            cd "$ROOT_DIR/implementations/java21springbootgraal"
            log_info "Building Java 21 Spring Boot GraalVM JAR (quick mode)..."
            mise exec "java@$JAVA_21_VERSION" -- mvn clean package -q -DskipTests
            log_success "Java 21 Spring Boot GraalVM JAR built successfully"
        fi

        # 7. Java 21 Spring Boot GraalVM (running on JVM for quick testing)
        run_test "java21SpringBootGraalReferenceService" 8098 \
            "mise exec java@$JAVA_21_VERSION -- java -jar target/java21SpringBootGraalReferenceService.jar" \
            "$ROOT_DIR/implementations/java21springbootgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA21_SPRINGBOOT_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    fi

    # Build shared Java 25 library (needed by all Java 25 services)
    log_section "Building Shared Java 25 Library"
    if [ -d "$ROOT_DIR/implementations/java25.events" ]; then
        if [ -z "$JAVA_25_VERSION" ]; then
            log_error "Java 25 not found in mise! Please install it first:"
            log_error "  Run: ./setup-environment.sh"
            log_error "  Or manually: mise install java@25"
            exit 1
        fi
        log_info "Using Java 25 ($JAVA_25_VERSION)"
        cd "$ROOT_DIR/implementations/java25.events"
        log_info "Building java25.events library..."
        mise exec "java@$JAVA_25_VERSION" -- mvn clean install -q
        log_success "Java 25 library built successfully"
    fi

    # Build Java 25 Quarkus GraalVM
    if [ "$GRAAL_MODE" = "native" ]; then
        log_section "Building Java 25 Quarkus GraalVM Native Binary (Experimental)"
        if [ -d "$ROOT_DIR/implementations/java25quarkusgraal" ]; then
            log_info "Using Java 25 ($JAVA_25_VERSION)"
            cd "$ROOT_DIR/implementations/java25quarkusgraal"
            log_info "Building Java 25 Quarkus GraalVM native binary (this will take ~5 minutes)..."
            log_warn "Java 25 is not officially supported by Quarkus - may have warnings"
            mise exec "java@$JAVA_25_VERSION" -- mvn package -Pnative -q -DskipTests -Dnet.bytebuddy.experimental=true
            log_success "Java 25 Quarkus GraalVM native binary built successfully"
        fi

        # 8. Java 25 Quarkus GraalVM (fast startup with native executable) [Experimental]
        run_test "java25QuarkusReferenceService" 8103 \
            "./target/java25QuarkusReferenceService-runner" \
            "$ROOT_DIR/implementations/java25quarkusgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA25_QUARKUS_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    else
        log_section "Building Java 25 Quarkus GraalVM JAR (Experimental)"
        if [ -d "$ROOT_DIR/implementations/java25quarkusgraal" ]; then
            log_info "Using Java 25 ($JAVA_25_VERSION)"
            cd "$ROOT_DIR/implementations/java25quarkusgraal"
            log_info "Building Java 25 Quarkus GraalVM JAR (quick mode)..."
            log_warn "Java 25 is not officially supported by Quarkus - may have warnings"
            mise exec "java@$JAVA_25_VERSION" -- mvn clean package -q -DskipTests -Dnet.bytebuddy.experimental=true
            log_success "Java 25 Quarkus GraalVM JAR built successfully"
        fi

        # 8. Java 25 Quarkus GraalVM (running on JVM for quick testing) [Experimental]
        run_test "java25QuarkusReferenceService" 8103 \
            "mise exec java@$JAVA_25_VERSION -- java -jar target/java25QuarkusReferenceService.jar" \
            "$ROOT_DIR/implementations/java25quarkusgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA25_QUARKUS_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    fi

    # Build Java 25 Spring Boot JAR
    log_section "Building Java 25 Spring Boot JAR"
    if [ -d "$ROOT_DIR/implementations/java25springboot" ]; then
        log_info "Using Java 25 ($JAVA_25_VERSION)"
        cd "$ROOT_DIR/implementations/java25springboot"
        log_info "Building Java 25 Spring Boot JAR..."
        mise exec "java@$JAVA_25_VERSION" -- mvn clean package -q -DskipTests
        log_success "Java 25 Spring Boot JAR built successfully"
    fi

    # 9. Java 25 Spring Boot (fast startup with pre-built JAR)
    run_test "java25SpringBootReferenceService" 8101 \
        "mise exec java@$JAVA_25_VERSION -- java -jar target/java25SpringBootReferenceService.jar" \
        "$ROOT_DIR/implementations/java25springboot" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_JAVA25_SPRINGBOOT" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build Java 25 Spring Boot GraalVM
    if [ "$GRAAL_MODE" = "native" ]; then
        log_section "Building Java 25 Spring Boot GraalVM Native Binary"
        if [ -d "$ROOT_DIR/implementations/java25springbootgraal" ]; then
            log_info "Using Java 25 ($JAVA_25_VERSION)"
            cd "$ROOT_DIR/implementations/java25springbootgraal"
            log_info "Building Java 25 Spring Boot GraalVM native binary (this will take ~5 minutes)..."
            mise exec "java@$JAVA_25_VERSION" -- mvn clean package -Pnative -q -DskipTests
            log_success "Java 25 Spring Boot GraalVM native binary built successfully"
        fi

        # 10. Java 25 Spring Boot GraalVM (fast startup with native executable)
        run_test "java25SpringBootGraalReferenceService" 8102 \
            "./target/java25SpringBootGraalReferenceService" \
            "$ROOT_DIR/implementations/java25springbootgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA25_SPRINGBOOT_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    else
        log_section "Building Java 25 Spring Boot GraalVM JAR"
        if [ -d "$ROOT_DIR/implementations/java25springbootgraal" ]; then
            log_info "Using Java 25 ($JAVA_25_VERSION)"
            cd "$ROOT_DIR/implementations/java25springbootgraal"
            log_info "Building Java 25 Spring Boot GraalVM JAR (quick mode)..."
            mise exec "java@$JAVA_25_VERSION" -- mvn clean package -q -DskipTests
            log_success "Java 25 Spring Boot GraalVM JAR built successfully"
        fi

        # 10. Java 25 Spring Boot GraalVM (running on JVM for quick testing)
        run_test "java25SpringBootGraalReferenceService" 8102 \
            "mise exec java@$JAVA_25_VERSION -- java -jar target/java25SpringBootGraalReferenceService.jar" \
            "$ROOT_DIR/implementations/java25springbootgraal" \
            "$MAX_SERVICE_STARTUP_WAIT"
        unbind_queue "$QUEUE_NAME_JAVA25_SPRINGBOOT_GRAAL" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true
    fi

    # 11. Python (interpreted - no build needed)
    run_test "pythonReferenceService" 8099 \
        "./pythonReferenceService" \
        "$ROOT_DIR/implementations/python" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_PYTHON" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Build Rust binary
    log_section "Building Rust Binary"
    if [ -d "$ROOT_DIR/implementations/rust" ]; then
        cd "$ROOT_DIR/implementations/rust"
        log_info "Building Rust release binary..."
        cargo build --release -q
        log_success "Rust binary built successfully"
    fi

    # 12. Rust (fast startup with pre-built binary)
    run_test "rustReferenceService" 8100 \
        "./target/release/rustReferenceService" \
        "$ROOT_DIR/implementations/rust" \
        "$MAX_SERVICE_STARTUP_WAIT"
    unbind_queue "$QUEUE_NAME_RUST" "$RABBITMQ_EXCHANGE" "$RABBITMQ_ROUTING_KEY" || true

    # Summary
    log_section "All Tests Completed!"
    log_success "Test results are available in: $RESULTS_FOLDER"

    # Generate comparison report
    log_section "Generating Comparison Report"
    if dotnet run --project "$DOTNET_TESTER_DIR" -- compare --folder "$RESULTS_FOLDER"; then
        log_success "Comparison report generated successfully"

        # Find the most recent comparison report
        latest_report=$(ls -t "$RESULTS_FOLDER"/test-report-comparison-*.md 2>/dev/null | head -1)
        if [ -n "$latest_report" ]; then
            log_info "Comparison report: $latest_report"
        fi
    else
        log_warn "Failed to generate comparison report"
        log_info "You can manually run: dotnet run --project \"$DOTNET_TESTER_DIR\" -- compare --folder \"$RESULTS_FOLDER\""
    fi
}

# Run main function
main "$@"
