# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a comprehensive performance comparison of **11 identical microservice implementations** across different programming languages, frameworks, and runtime configurations. Each service follows an identical specification to enable fair, apples-to-apples comparison of framework efficiency, resource usage, and developer experience.

**Core Reference Service Specification:**
- Consumes events from RabbitMQ (`instrument.status.changed`)
- Persists data to PostgreSQL (dedicated database per service)
- Publishes processed events back to RabbitMQ (`instrumentstatus.kpi.updated`)
- Exposes HTTP API endpoint: `GET /kpi` (returns latest record)

**Design Philosophy:** Framework defaults, no custom optimization, idiomatic patterns, no batching/caching. This measures "out of the box" performance, not highly-tuned edge cases.

## Repository Structure

```
/implementations/           # All service implementations
├── bun/                   # Bun + Prisma (port 8090)
├── dotnet9/               # .NET 9 with EF Core (port 8092)
├── dotnet9.events/        # Shared .NET event models library
├── dotnet9aot/            # .NET 9 Native AOT with raw SQL (port 8093)
├── go/                    # Go 1.23 + GORM (port 8094)
├── java21.events/         # Shared Java 21 event models library
├── java21quarkusgraal/    # Quarkus + GraalVM Native (port 8096)
├── java21springboot/      # Spring Boot on JVM (port 8097)
├── java21springbootgraal/ # Spring Boot + GraalVM Native (port 8098)
├── java25.events/         # Shared Java 25 event models library
├── java25springboot/      # Spring Boot on JVM (port 8101)
├── java25springbootgraal/ # Spring Boot + GraalVM Native (port 8102)
├── java25quarkusgraal/    # Quarkus + GraalVM Native (port 8103, experimental)
├── python/                # Python 3.13+ + SQLAlchemy + pika (port 8099)
└── rust/                  # Rust + Actix-web + SeaORM (port 8100)

/performance-tester/       # Load testing and comparison tools
├── service-tester.py      # Main performance test orchestrator
├── compare_test_results.py # Generate comparison reports
└── test-results/          # Test output and reports

/scripts/                  # All automation scripts
├── common.sh              # Shared library functions
├── infrastructure/        # Infrastructure and setup
│   ├── docker-compose.yml # PostgreSQL + RabbitMQ
│   ├── create-databases.sql # Database creation script
│   └── setup/             # Environment setup scripts
│       ├── setup-environment.sh
│       ├── verify-environment.sh
│       ├── uninstall-environment.sh
│       └── install-graalvm.sh
└── tools/                 # Build and test automation
    ├── build/
    │   ├── test-all-builds.sh # Verify all implementations build
    │   └── clean-binaries.sh  # Remove build artifacts
    └── testing/
        └── run-all-tests.sh   # Automated test suite (all services)

/                          # Root
└── run.sh                 # Complete pipeline: setup → verify → build → test
```

## Building Services

### Build Commands by Implementation

**Shared Libraries First** (for Java services):
```bash
# Java 21 shared events library
cd implementations/java21.events && mvn clean install

# Java 25 shared events library
cd implementations/java25.events && mvn clean install

# .NET 9 shared events library
cd implementations/dotnet9.events && dotnet build
```

**Individual Service Builds:**

**Bun:**
```bash
cd implementations/bun
bun install                    # Install dependencies (Prisma)
bunx prisma generate           # Generate Prisma client
bun build --compile index.ts --outfile bunReferenceService
```

**.NET 9 JIT:**
```bash
cd implementations/dotnet9
dotnet build -c Release
```

**.NET 9 Native AOT:**
```bash
cd implementations/dotnet9aot
dotnet publish -c Release
# Binary: bin/Release/net9.0/linux-x64/publish/dotnet9AotReferenceService
```

**Go:**
```bash
cd implementations/go
go build                       # Produces: goReferenceService
# Or: make build
```

**Java Spring Boot (JVM):**
```bash
cd implementations/java21springboot
mvn clean package             # Produces: target/*.jar
```

**Java Spring Boot (GraalVM Native):**
```bash
cd implementations/java21springbootgraal
mvn clean package -Pnative -DskipTests
# Takes ~5-10 minutes
# Produces: target/java21SpringBootGraalReferenceService
```

**Java Quarkus (GraalVM Native):**
```bash
cd implementations/java21quarkusgraal
mvn clean package -Pnative -DskipTests
# Produces: target/quarkus-app/quarkus-run.jar
```

**Python:**
```bash
cd implementations/python
# No build required - interpreted
python3 -m pip install -r requirements.txt  # Install dependencies
```

**Rust:**
```bash
cd implementations/rust
cargo build --release
# Produces: target/release/rustReferenceService
```

### Quick Build Verification

```bash
# Test that all implementations compile/build successfully
./scripts/tools/build/test-all-builds.sh

# Show detailed output
./scripts/tools/build/test-all-builds.sh --verbose

# Stop at first failure
./scripts/tools/build/test-all-builds.sh --stop-on-error
```

### Clean Build Artifacts

```bash
# Remove all binaries and build artifacts (shows before/after sizes)
./scripts/tools/build/clean-binaries.sh
```

## Running Services

### Infrastructure Prerequisites

**Start Docker containers:**
```bash
docker-compose -f scripts/infrastructure/docker-compose.yml up -d
```

**Create databases:**
```bash
docker exec -i performancetest-postgres psql -U admin -d postgres < scripts/infrastructure/create-databases.sql
```

### Running Individual Services

Each service runs on its designated port. Examples:

**Go:**
```bash
cd implementations/go
./goReferenceService
# Listens on: http://localhost:8094
```

**.NET 9 AOT:**
```bash
cd implementations/dotnet9aot
./bin/Release/net9.0/linux-x64/publish/dotnet9AotReferenceService
# Listens on: http://localhost:8093
```

**Java Spring Boot Native:**
```bash
cd implementations/java21springbootgraal
./target/java21SpringBootGraalReferenceService
# Listens on: http://localhost:8098
```

**Python:**
```bash
cd implementations/python
python3 main.py
# Process shows as: pythonReferenceService
# Listens on: http://localhost:8099
```

**Rust:**
```bash
cd implementations/rust
./target/release/rustReferenceService
# Listens on: http://localhost:8100
```

## Performance Testing

### Automated Testing (All Services)

The `run-all-tests.sh` script automates:
1. Database recreation
2. RabbitMQ queue clearing
3. Building each service
4. Running performance tests
5. Generating comparison reports

```bash
# Quick test (100 events, 5 seconds API load)
./scripts/tools/testing/run-all-tests.sh --events 100 --duration 5s

# Standard benchmark (10,000 events, 30 seconds API load)
./scripts/tools/testing/run-all-tests.sh --events 10000 --duration 30s

# Heavy load test
./scripts/tools/testing/run-all-tests.sh --events 50000 --duration 2m --workers 4

# Native GraalVM builds (slower build, more accurate benchmarks)
./scripts/tools/testing/run-all-tests.sh --native
```

**Options:**
- `-e, --events NUM` - Number of events to publish/consume (default: 10000)
- `-d, --duration TIME` - API load test duration (e.g., 5s, 1m, 2h) (default: 30s)
- `-w, --workers NUM` - Concurrent API workers (default: 1)
- `-n, --native` - Build GraalVM services as native executables (default: JAR mode)

### Manual Testing (Single Service)

**Requirements:**
- Service running on its port
- Python 3.13+ (minimum 3.9)
- k6 installed (`sudo snap install k6`)
- Python packages: `pip install -r performance-tester/requirements.txt`

```bash
cd performance-tester

# Test specific service (must be running)
python service-tester.py --port 8094 --events 1000 --api-duration 30s --api-workers 1

# Custom configuration
python service-tester.py --port 8099 --events 5000 --api-duration 2m --api-workers 100
```

**Test Phases:**
1. **Publish** - Send events to RabbitMQ as fast as possible
2. **Consume** - Wait for service to process all events (measures throughput)
3. **API Load** - Use k6 to push maximum sustained load for specified duration

**Output:** `test-results/` directory contains files named `test-report-{timestamp}-{servicename}.{type}`:
- `test-report-{timestamp}-{servicename}.json` - Main summary
- `test-report-{timestamp}-{servicename}.resource-metrics.json` - CPU/memory data
- `test-report-{timestamp}-{servicename}.events-throughput.json` - Event processing metrics
- `test-report-{timestamp}-{servicename}.api-throughput.json` - API call metrics
- `test-report-{timestamp}-{servicename}.postgres-metrics.json` - PostgreSQL metrics
- `test-report-{timestamp}-{servicename}.rabbitmq-metrics.json` - RabbitMQ metrics
- `test-report-{timestamp}-{servicename}.system-metrics.json` - System-wide metrics
- `test-report-{timestamp}-{servicename}.chart.png` - Combined visualization (3 subplots)

Example: `test-report-20251022_122306-bunreferenceservice.json`

### Comparing Test Results

```bash
cd performance-tester

# Generate comparison report from all results in test-results/
python compare_test_results.py

# Custom results folder
python compare_test_results.py --folder /path/to/results

# Print to stdout
python compare_test_results.py --stdout
```

## Complete Orchestration Pipeline

### Single-Command Setup and Testing

For a completely automated workflow from environment setup to test results:

```bash
./run.sh
```

**Pipeline Stages:**
1. **Environment Setup** - Runs `./scripts/infrastructure/setup/setup-environment.sh --non-interactive`
2. **Verification** - Runs `./scripts/infrastructure/setup/verify-environment.sh`
3. **Build All Services** - Runs `./scripts/tools/build/test-all-builds.sh`
4. **Performance Testing** - Runs `./scripts/tools/testing/run-all-tests.sh --events 20000 --duration 60s --native --results-folder ./test-results-{timestamp}`
5. **Report Generation** - Comparison reports generated automatically

**Default Configuration:**
- Events: 20,000
- API Duration: 60 seconds
- GraalVM Mode: Native builds
- Results Folder: `./test-results-{timestamp}/` (timestamped for each run)

**Key Features:**
- Non-interactive mode (no prompts)
- Stops immediately on any failure
- Logs to `run-all-setup-test.log`
- Activates mise for script session automatically
- Prints mise activation instructions at completion

**Prerequisites:**
- Docker infrastructure running (`docker-compose -f scripts/infrastructure/docker-compose.yml up -d`)
- Databases created (`docker exec -i performancetest-postgres psql -U admin -d postgres < scripts/infrastructure/create-databases.sql`)

**After Completion:**
Users must activate mise in their current shell:
```bash
source ~/.bashrc
# or
eval "$(mise activate bash)"
```

New terminal sessions will have mise activated automatically.

## Key Architectural Differences

### .NET 9 AOT - Raw SQL
**Why:** EF Core has limited native AOT support due to heavy reflection usage.
- Uses `NpgsqlDataSource` (singleton) with raw SQL
- Source-generated JSON serialization (AOT-compatible)
- More manual code vs. ORM convenience, but better AOT performance

### Java Quarkus - Build-Time Optimization
- Build-time dependency injection (Arc CDI)
- Elastic connection pool (0-20 connections, auto-scaling)
- `@Blocking` annotation for thread pool management
- Reactive messaging with SmallRye

### Bun - Prisma ORM
**Why:** `bun:sqlite` only supports SQLite, not PostgreSQL.
- Uses Prisma ORM for PostgreSQL access
- Represents typical production Bun applications
- Could use `postgres.js` or `pg` for better performance, but Prisma provides fair ORM comparison

### Python - Explicit Transactions
- SQLAlchemy requires explicit `session.commit()` and `session.rollback()`
- `pool_pre_ping=True` for connection health checks
- Default pool sizing (5 pool, 10 overflow)
- Synchronous blocking (no async)

### All Other Services
- **Transaction Strategy:** Implicit auto-commit per operation
- **Pooling:** Framework defaults
- **ORM:** Standard for each ecosystem (GORM, SeaORM, EF Core, Hibernate/JPA)

## Important Design Constraints

These are **intentional** for fair comparison - do NOT change unless updating all services:

**Configuration Management:**
- ✅ **ALL connection configuration MUST use environment variables** (PostgreSQL, RabbitMQ)
- ✅ Environment variables MUST have sensible defaults matching docker-compose.yml
- ❌ **NO hardcoded credentials or connection strings** (host, port, username, password)
- ✅ Each service must include a `.env.example` file documenting all configurable variables
- ✅ Services must work identically with or without a `.env` file (backwards compatible)

**Database Access:**
- ❌ No batching (one message = one INSERT)
- ❌ No explicit transactions (use framework auto-commit, except Python)
- ❌ No caching (no in-memory or query result caching)
- ✅ Framework defaults for connection pooling

**RabbitMQ Configuration:**
- **Prefetch count: 50** (standardized across all services for fair comparison)
  - Exception: Bun uses prefetch 1 due to framework restrictions
- **Manual acknowledgment** - ACK only after DB persistence

**Framework & Libraries:**
- ✅ Use industry-standard libraries
- ✅ Follow idiomatic patterns
- ✅ Framework default configurations
- ❌ No custom tuning or optimization

**Code Quality:**
- ✅ **Zero or minimal compiler warnings** - All implementations must compile/build with no warnings
- ✅ Language-specific best practices:
  - **.NET:** Enable nullable reference types, no IL trimming warnings for AOT
  - **TypeScript/Bun:** No unused variables or parameters
  - **Go:** Check all error returns, use `go vet` and `go fmt`
  - **Java:** No unchecked operations, deprecation warnings, or raw types
  - **Python:** Follow PEP 8, use type hints, consistent naming conventions
  - **Rust:** Zero `cargo` warnings, pass `cargo clippy` checks
- ✅ Fix warnings that indicate potential bugs or code quality issues
- ✅ Only suppress warnings when genuinely justified with clear documentation

## Common Development Tasks

### Adding a New Implementation

1. Follow reference service specification exactly
2. Use idiomatic patterns for the language/framework
3. Use framework defaults (no custom optimization)
4. **Configure all connection settings via environment variables:**
   - PostgreSQL: `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`
   - RabbitMQ: `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USER`, `RABBITMQ_PASS`
   - Server port: `SERVER_PORT` (or framework-specific equivalent)
   - All variables must have defaults matching docker-compose.yml
   - **NO hardcoded credentials or connection strings allowed**
5. Create a `.env.example` file in the service directory documenting all environment variables
6. Create dedicated PostgreSQL database (see `create-databases.sql`)
7. Assign unique port number
8. Document any necessary deviations in service README
9. Add build commands to `test-all-builds.sh`
10. Add run commands to `run-all-tests.sh`
11. Add cleanup commands to `clean-binaries.sh`
12. Add runtime/SDK/dependencies to environment scripts:
    - `setup-environment.sh` - Installation steps
    - `verify-environment.sh` - Version checks and validation
    - `uninstall-environment.sh` - Cleanup steps

### Troubleshooting Database Issues

**Database Lifecycle Philosophy:** This project uses a "create once, clean many times" approach. Databases are created once and persist between test runs. Only table data is cleared between tests.

```bash
# Clear data from a specific service's database
cd performance-tester
./clean-service-data.sh go_db        # Clear Go service database
./clean-service-data.sh dotnet9_db   # Clear .NET 9 service database
# ... etc

# Manual database reset (only if absolutely necessary)
# Note: This is rarely needed - the test scripts handle cleanup automatically
docker exec performancetest-postgres psql -U admin -d postgres -c "DROP DATABASE IF EXISTS dotnet9_db;"
docker exec -i performancetest-postgres psql -U admin -d postgres < scripts/infrastructure/create-databases.sql
```

**Key Changes:**
- `run-all-tests.sh` no longer drops/recreates databases (saves ~20-30 seconds per run)
- `create-databases.sql` uses `CREATE DATABASE IF NOT EXISTS` (idempotent)
- Services create their own table schemas on startup (code-first approach)
- `clean-service-data.sh` dynamically discovers and truncates tables (works for all services)

### Clearing RabbitMQ Queues

```bash
cd performance-tester
./clear-rabbitmq.sh
```

### Viewing Service Logs

```bash
# Docker infrastructure
docker logs performancetest-postgres
docker logs performancetest-rabbitmq

# RabbitMQ Management UI
# http://localhost:15672 (admin/admin)
```

## Environment Setup

### Automated Installation (Recommended)

```bash
# Install all required tools and runtimes
./scripts/infrastructure/setup/setup-environment.sh

# Verify installation
./scripts/infrastructure/setup/verify-environment.sh

# Test all implementations build correctly
./scripts/tools/build/test-all-builds.sh
```

**Installs:**
- mise (polyglot tool version manager)
- Java 21/25 (GraalVM distributions with Native Image support)
- Apache Maven
- Go 1.23+
- Rust
- .NET 9 SDK
- Bun runtime
- Python 3.13+ with pip
- k6 (for performance testing via snap)

### Uninstall

```bash
# Remove user-installed tools (keeps system packages)
./scripts/infrastructure/setup/uninstall-environment.sh

# Remove everything including system packages
./scripts/infrastructure/setup/uninstall-environment.sh --full
```

### mise and Java Version Management

**Important:** This project requires **both Java 21 and Java 25** to be installed. The `run-all-tests.sh` script automatically switches between versions.

**mise Overview:**

mise (https://mise.jdx.dev) is a polyglot tool version manager that replaces multiple version managers (SDKMAN, rustup, nvm, etc.) with a single tool. It provides:
- Fast startup (<100ms vs 2-5s for traditional version managers)
- Directory-based automatic version switching (via `.mise.toml`)
- Per-command version execution (`mise exec java@21 -- mvn clean install`)
- Unified management for all project runtimes

**Verify mise Installation:**
```bash
# Check mise is installed and working
mise version

# List all installed tools
mise list

# Check Java versions
mise list java
# Expected output:
# java    graalvm-jdk-21.0.2  ~/.local/share/mise/installs/java/graalvm-jdk-21.0.2
# java    graalvm-jdk-25.0.1  ~/.local/share/mise/installs/java/graalvm-jdk-25.0.1
```

**How the Scripts Work (mise exec):**

All project scripts (`run-all-tests.sh` and `test-all-builds.sh`) use **`mise exec`** exclusively to run commands with specific Java versions. This is the recommended pattern for scripts and automation:

```bash
# Scripts automatically detect installed GraalVM versions
JAVA_21_VERSION=$(mise list java 2>/dev/null | grep "graalvm-jdk-21" | awk '{print $2}' | head -1)
JAVA_25_VERSION=$(mise list java 2>/dev/null | grep "graalvm-jdk-25" | awk '{print $2}' | head -1)

# Then execute builds with the correct version using mise exec
mise exec "java@$JAVA_21_VERSION" -- mvn clean install  # For Java 21 projects
mise exec "java@$JAVA_25_VERSION" -- mvn clean install  # For Java 25 projects
```

**Benefits of `mise exec`:**
- Each command runs in isolated environment with specified Java version
- No modification to global shell environment
- Safer for scripts and automation
- Works correctly in non-interactive mode

**Manual Interactive Usage (Optional Convenience):**

For manual terminal work, you can also use `mise use` as a convenience feature. **Note: This is NOT used by project scripts.**

```bash
# Option 1: Execute a single command with specific version (same as scripts)
mise exec java@21 -- java -version
mise exec java@25 -- mvn clean install

# Option 2: Temporarily switch version in current shell (convenience only)
mise use java@21
java -version     # Now uses Java 21
mvn clean install # Also uses Java 21

# Switch back
mise use java@25
java -version     # Now uses Java 25
```

**Test Execution Order:**

The scripts ensure correct Java versions are used:

1. **Uses Java 21** → Builds `java21.events` library via `mise exec java@21`
2. **Uses Java 21** → Builds & tests `java21springboot`
3. **Uses Java 21** → Builds & tests `java21springbootgraal`
4. **Uses Java 21** → Builds & tests `java21quarkusgraal`
5. **Uses Java 25** → Builds `java25.events` library via `mise exec java@25`
6. **Uses Java 25** → Builds & tests `java25springboot`
7. **Uses Java 25** → Builds & tests `java25springbootgraal`
8. **Uses Java 25** → Builds & tests `java25quarkusgraal`

The scripts will **fail immediately** if:
- Java 21 or Java 25 are not installed
- mise is not properly configured
- GraalVM distributions are not found

**Troubleshooting:**

If you see errors like "Java 25 (69) is not supported by Byte Buddy":
- This means Java 25 is being used to build Java 21 services
- The scripts should prevent this, but if it happens:
  1. Check that both Java versions are installed: `mise list java`
  2. Ensure mise is activated: `eval "$(mise activate bash)"`
  3. Verify GraalVM installations: `ls ~/.local/share/mise/installs/java/`
  4. Re-run setup if needed: `./scripts/infrastructure/setup/setup-environment.sh`

## Reference Documentation

**Performance Testing Details:** See `performance-tester/README.md` for:
- Detailed tool documentation
- Understanding variability metrics (CV%, Std Dev)
- Troubleshooting test failures
- Test result interpretation

## Port Assignments

Each implementation has a dedicated port:
- 8090: Bun
- 8092: .NET 9 JIT
- 8093: .NET 9 AOT
- 8094: Go
- 8096: Java 21 Quarkus GraalVM
- 8097: Java 21 Spring Boot JVM
- 8098: Java 21 Spring Boot GraalVM
- 8099: Python
- 8100: Rust
- 8101: Java 25 Spring Boot JVM
- 8102: Java 25 Spring Boot GraalVM
- 8103: Java 25 Quarkus GraalVM (experimental)

## Docker Services

```bash
# Start infrastructure
docker-compose -f scripts/infrastructure/docker-compose.yml up -d

# Services provided:
# - PostgreSQL: localhost:5432 (admin/admin)
# - RabbitMQ: localhost:5672 (admin/admin)
# - RabbitMQ Management: http://localhost:15672 (admin/admin)

# Stop infrastructure
docker-compose -f scripts/infrastructure/docker-compose.yml down
```

## Testing Individual Endpoints

```bash
# Test any service's /kpi endpoint
curl http://localhost:8094/kpi    # Go
curl http://localhost:8093/kpi    # .NET 9 AOT
curl http://localhost:8099/kpi    # Python

# Or use the test.http file (requires REST client)
```
