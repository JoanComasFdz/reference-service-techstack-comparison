# Reference Service - Tech Stack Performance Comparison

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![Languages](https://img.shields.io/badge/languages-6-orange.svg)]()
[![Implementations](https://img.shields.io/badge/tech%20stacks-12-green.svg)]()

A comprehensive performance comparison of identical microservice implementations across 12 different tech stacks, frameworks, and runtime configurations. Each implementation follows the same specification to enable fair, apples-to-apples comparison of framework efficiency, resource usage, and developer experience.

## Overview

This project implements a reference microservice that:

- Consumes events from RabbitMQ
- Persists data to PostgreSQL
- Publishes processed events back to RabbitMQ
- Exposes an HTTP API for data retrieval

Each implementation uses idiomatic patterns and framework defaults to represent real-world development practices, not highly-optimized edge cases.

## Table of Contents

- [Reference Service - Tech Stack Performance Comparison](#reference-service---tech-stack-performance-comparison)
  - [Overview](#overview)
  - [Table of Contents](#table-of-contents)
  - [🚀 Quick Start](#-quick-start)
  - [Reference Service Specification](#reference-service-specification)
    - [Core Functionality](#core-functionality)
    - [Database Setup](#database-setup)
  - [Comparison Methodology](#comparison-methodology)
    - [Design Philosophy](#design-philosophy)
    - [What This Measures](#what-this-measures)
    - [What This Does NOT Measure](#what-this-does-not-measure)
  - [Implementations](#implementations)
  - [Key Architectural Differences](#key-architectural-differences)
    - [.NET 9 AOT - Raw SQL](#net-9-aot---raw-sql)
    - [Java Quarkus - Build-Time Optimization](#java-quarkus---build-time-optimization)
    - [Bun - Prisma ORM](#bun---prisma-orm)
    - [Python - Explicit Transactions](#python---explicit-transactions)
    - [All Other Services](#all-other-services)
  - [🚀 Complete Pipeline - Setup to Test Results (One Command)](#-complete-pipeline---setup-to-test-results-one-command)
  - [Prerequisites](#prerequisites)
    - [Required Infrastructure](#required-infrastructure)
    - [mise](#mise)
  - [Project Structure](#project-structure)
  - [Disk space](#disk-space)
    - [mise installation](#mise-installation)
    - [Implementations with native images](#implementations-with-native-images)
  - [Performance Testing Approach](#performance-testing-approach)
    - [Test Phases](#test-phases)
      - [Phase 1: Event Processing](#phase-1-event-processing)
      - [Phase 2: API Load Testing](#phase-2-api-load-testing)
      - [Phase 3: Resource Profiling](#phase-3-resource-profiling)
    - [Metrics Collected](#metrics-collected)
    - [Rules of Engagement - Official Benchmarks](#rules-of-engagement---official-benchmarks)
  - [Implementation Guidelines](#implementation-guidelines)
    - [Database Access](#database-access)
    - [Framework \& Libraries](#framework--libraries)
    - [Configuration](#configuration)
  - [Contributing](#contributing)
  - [License](#license)
  - [Acknowledgments](#acknowledgments)

## 🚀 Quick Start

```bash
# 1. Run complete pipeline (setup → build → test)
./run.sh

# Results in: ./test-results-{timestamp}/
```

## Reference Service Specification

### Core Functionality

Each service must:

1. **Ensure RabbitMQ infrastructure** exists on startup (exchange, queues, bindings)
2. **Subscribe to events** from RabbitMQ:
   - Event: `instrument.status.changed`
   - Exchange: `referenceservice.comparison`
   - Prefetch: **50 messages** (standardized across all services for fair comparison)
     - Industry-standard value that prevents RabbitMQ from becoming the bottleneck
     - With 10ms DB latency: 50 × (1/10ms) = ~5,000 msg/sec theoretical throughput
     - Ensures we're testing framework efficiency, not message broker constraints
     - Exception: Bun uses prefetch 1 due to framework restrictions
   - Acknowledgment: **Manual** (only after DB persistence)
3. **Persist to PostgreSQL**:
   - Store event payload in a dedicated database
   - Each service has its own database for isolation
4. **Publish processed events**:
   - Event: `instrumentstatus.kpi.updated`
   - Exchange: `referenceservice.comparison`
5. **Expose HTTP API**:
   - `GET /kpi` - Returns the **latest** record from the database

### Database Setup

Each service uses a dedicated PostgreSQL database:

## Comparison Methodology

### Design Philosophy

This comparison prioritizes **fairness over performance**:

- ✅ **Framework defaults** - No custom tuning or optimization
- ✅ **Idiomatic patterns** - Code written as the framework recommends
- ✅ **No batching** - One message = one database INSERT
- ✅ **No caching** - No in-memory caching or query result caching
- ✅ **Manual acknowledgment** - Messages ACKed only after DB persistence
- ✅ **Similar abstraction levels** - Most services use ORMs (except where incompatible)

### What This Measures

- **Framework efficiency** with default configurations
- **Runtime performance** (throughput, latency, CPU usage)
- **Memory footprint** under load
- **CPU efficiency** under load

### What This Does NOT Measure

- ❌ Highly optimized production configurations
- ❌ Batch processing capabilities
- ❌ Caching strategies
- ❌ Connection pool tuning
- ❌ Horizontal scalability
- ❌ Custom performance optimizations

## Implementations

| Language/Framework | Runtime | Port | Location |
|--------------------|---------|------|----------|
| **Bun** | Native (compiled) | 8090 | `implementations/bun/` |
| **.NET 9** | JIT | 8092 | `implementations/dotnet9/` |
| **.NET 9 AOT** | Native AOT | 8093 | `implementations/dotnet9aot/` |
| **Go 1.23** | Native | 8094 | `implementations/go/` |
| **Java 21 Quarkus** | GraalVM Native | 8096 | `implementations/java21quarkusgraal/` |
| **Java 21 Spring Boot** | JVM | 8097 | `implementations/java21springboot/` |
| **Java 21 Spring Boot** | GraalVM Native | 8098 | `implementations/java21springbootgraal/` |
| **Python 3.13+** | Interpreted | 8099 | `implementations/python/` |
| **Rust** | Native | 8100 | `implementations/rust/` |
| **Java 25 Spring Boot** | JVM | 8101 | `implementations/java25springboot/` |
| **Java 25 Spring Boot** | GraalVM Native | 8102 | `implementations/java25springbootgraal/` |
| **Java 25 Quarkus** ⚠️ | GraalVM Native | 8103 | `implementations/java25quarkusgraal/` |

**Shared Libraries:**

- `implementations/java21.events/` - Shared Java 21 event models
- `implementations/java25.events/` - Shared Java 25 event models
- `implementations/dotnet9.events/` - Shared .NET event models

⚠️ **Note**: Java 25 Quarkus is experimental - Quarkus does not officially support Java 25 yet.

## Key Architectural Differences

### .NET 9 AOT - Raw SQL

**Why:** EF Core has limited native AOT support due to heavy reflection usage.

**Approach:**

- Uses raw SQL with `NpgsqlDataSource` (singleton)
- Source-generated JSON serialization
- More manual code vs. ORM convenience

### Java Quarkus - Build-Time Optimization

**Why:** Designed specifically for cloud-native, GraalVM-first development.

**Approach:**

- Build-time dependency injection (Arc CDI)
- Elastic connection pool (0-20 connections, auto-scaling)
- `@Blocking` annotation for explicit thread pool management
- Reactive messaging with SmallRye
- **Transaction handling:** Uses implicit auto-commit (no `@Transactional`)
  - Quarkus developers typically use `@Transactional` as best practice
  - Intentionally removed for fair comparison with other services
  - Eliminates transaction overhead as a comparison variable
  - All services now use identical implicit auto-commit semantics

### Bun - Prisma ORM

**Why:** `bun:sqlite` only supports SQLite, not PostgreSQL.

**Approach:**

- Uses Prisma ORM for PostgreSQL access
- Bun.serve() for native HTTP server
- Direct TypeScript execution (no transpilation)
- Represents typical production Bun applications

**Note:** Could use `postgres.js` or `pg` for better performance, but Prisma provides fair ORM comparison.

### Python - Explicit Transactions

**Why:** SQLAlchemy's session management requires explicit commits.

**Approach:**

- Explicit `session.commit()` and `session.rollback()`
- `pool_pre_ping=True` for connection health checks
  - Recommended SQLAlchemy best practice for production
  - Tests connection health before use (lightweight query)
  - Prevents "connection already closed" errors
  - Handles database server restarts gracefully
  - Minimal overhead (~1ms per checkout)
- Default pool sizing (5 pool, 10 overflow)
- Synchronous blocking (no async)

### All Other Services

- **Transaction Strategy:** Implicit auto-commit per operation
- **Pooling:** Framework defaults
- **ORM:** Standard for each ecosystem (GORM, SeaORM, EF Core, Hibernate/JPA)

## 🚀 Complete Pipeline - Setup to Test Results (One Command)

For a completely automated experience from zero to test results:

```bash
./run.sh
```

**What it does:**

1. Installs all tools and runtimes (mise, Java 21/25, Go, Rust, .NET, Bun, Python, k6)
2. Verifies the installation
3. Builds all 12 service implementations
4. Runs comprehensive performance tests
5. Generates comparison reports

**Default configuration:**

- Events: 20,000
- API Duration: 60 seconds
- Mode: Native GraalVM builds
- Results: `./test-results-{timestamp}/`

**Note:** Assumes Docker infrastructure is already running (`docker-compose up -d`).

## Prerequisites

### Required Infrastructure

**Docker & Docker Compose** - For PostgreSQL and RabbitMQ:

```bash
docker-compose -f scripts/infrastructure/docker-compose.yml up -d
```

### mise

**Required for:** All runtimes (Java 21/25, Go, Rust, .NET, Bun, Python, Maven)

[mise](https://mise.jdx.dev) is a unified tool version manager that replaces SDKMAN, rustup, nvm, and other version managers. The `setup-environment.sh` script installs mise and all required tools automatically.

**Why mise?**

- Fast startup (<100ms vs 2-5s for traditional managers)
- Single tool manages all runtimes (Java, Go, Rust, .NET, Python, etc.)
- Directory-based version switching (automatic via `.mise.toml`)
- Per-command version execution (`mise exec java@21 -- mvn clean install`)

**How project scripts work:**

All project scripts (`run-all-tests.sh` and `test-all-builds.sh`) use **`mise exec`** exclusively:

```bash
# Scripts use this pattern (recommended for automation)
mise exec java@21 -- mvn clean install
mise exec java@25 -- java -version
```

**Benefits:** Isolated environment per command, no global state modification, works in non-interactive mode.

## Project Structure

```tree
.
├── implementations/              # All service implementations
│   ├── bun/                     # Bun + Prisma
│   ├── dotnet9/                 # .NET 9 with EF Core
│   ├── dotnet9.events/          # Shared .NET event models
│   ├── dotnet9aot/              # .NET 9 Native AOT with raw SQL
│   ├── go/                      # Go 1.23 + GORM
│   ├── java21.events/           # Shared Java 21 event models
│   ├── java21quarkusgraal/      # Quarkus + GraalVM Native
│   ├── java21springboot/        # Spring Boot on JVM
│   ├── java21springbootgraal/   # Spring Boot + GraalVM Native
│   ├── java25.events/           # Shared Java 25 event models
│   ├── java25quarkusgraal/      # Quarkus + GraalVM Native (experimental)
│   ├── java25springboot/        # Spring Boot on JVM
│   ├── java25springbootgraal/   # Spring Boot + GraalVM Native
│   ├── python/                  # Python 3.13+ + SQLAlchemy + pika
│   └── rust/                    # Rust + Actix-web + SeaORM
├── performance-tester-dotnet/   # .NET performance tester
│   ├── src/PerformanceTester.Cli/  # CLI application
│   ├── src/PerformanceTester.Core/ # Core library
│   └── test-results/            # Test output and reports
├── scripts/                     # All automation scripts
│   ├── common.sh                # Shared library functions
│   ├── infrastructure/          # Infrastructure and setup
│   │   ├── docker-compose.yml   # PostgreSQL + RabbitMQ
│   │   ├── create-databases.sql # Database creation script
│   │   └── setup/               # Environment setup scripts
│   │       ├── setup-environment.sh
│   │       ├── verify-environment.sh
│   │       ├── uninstall-environment.sh
│   │       └── install-graalvm.sh
│   └── tools/                   # Build and test automation
│       ├── build/
│       │   ├── test-all-builds.sh   # Verify all builds
│       │   └── clean-binaries.sh    # Clean build artifacts
│       └── testing/
│           └── run-all-tests.sh     # Automated test suite
├── logs/                        # Log files (gitignored)
├── run.sh                       # Master orchestration script
└── test.http                    # HTTP endpoint tests
```

## Disk space

### mise installation

```ascii
  ~/.local/share/mise/             [4.1 GB total]
  ├── installs/                    [4.1 GB]
  │   ├── rust/                    [1.6 GB] ████████████████
  │   ├── java/                    [1.3 GB] █████████████
  │   │   ├── graalvm-jdk-21.0.2/  [~650 MB] (with Native Image)
  │   │   └── graalvm-jdk-25.0.1/  [~650 MB] (with Native Image)
  │   ├── dotnet/                  [578 MB] ██████
  │   ├── python/                  [330 MB] ███
  │   ├── go/                      [269 MB] ███
  │   ├── bun/                     [100 MB] █
  │   └── maven/                   [11 MB]
  │
  ├── plugins/                     [396 KB]
  ├── downloads/                   [28 KB]
  └── shims/                       [4 KB]
  ~/.cache/mise/                   [144 KB]
```

  Legend:

  ```ascii
  ████ = ~100 MB per block
  ```

### Implementations with native images

```ascii
  implementations/                 [3.2 GB total]
  ├── rust/                        [1.4 GB] █████████████████████████████
  ├── bun/                         [374 MB] ████████
  ├── dotnet9aot/                  [369 MB] ███████
  ├── java25quarkusgraal/          [227 MB] █████ 
  ├── java21springbootgraal/       [221 MB] █████
  ├── java21quarkusgraal/          [211 MB] ████
  ├── java25springbootgraal/       [206 MB] ████
  ├── dotnet9/                     [62 MB]  █
  ├── java25springboot/            [53 MB]  █
  ├── java21springboot/            [46 MB]  █
  ├── go/                          [32 MB]  █
  ├── dotnet9.events/              [400 KB] (shared library)
  ├── java21.events/               [168 KB] (shared library)
  ├── java25.events/               [168 KB] (shared library)
  └── python/                      [56 KB]  (no build artifacts)
```

  Legend:
  
  ```ascii
   █ = ~50 MB per block
  ```

## Performance Testing Approach

### Test Phases

#### Phase 1: Event Processing

- Publishes N events to RabbitMQ
- Measures throughput (messages/second)
- Tracks memory usage during processing
- Validates data persistence

#### Phase 2: API Load Testing

- Uses k6 for HTTP load generation
- Tests `/kpi` endpoint under sustained load
- Measures latency (p50, p95, p99)
- Tracks CPU and memory

#### Phase 3: Resource Profiling

- Baseline memory (idle state)
- Peak memory (under load)
- CPU utilization
- Startup time

### Metrics Collected

- **Throughput:** Messages processed per second
- **Latency:** API response times (p50, p95, p99)
- **Memory:** RSS at baseline and peak
- **CPU:** Utilization percentage
- **Startup Time:** From launch to ready state
- **Binary Size:** Compiled executable size
- **Database Performance:** Query execution times

### Rules of Engagement - Official Benchmarks

For reproducible benchmark results and instructions on contributing your hardware results, see [results/README.md](results/README.md).

## Implementation Guidelines

Services must follow these rules for fair comparison:

### Database Access

- ❌ No batching - One message = one INSERT
- ❌ No explicit transactions - Use framework auto-commit (except Python)
  - **Why no transactions?**
    1. Single INSERT per message - atomicity is inherent in one SQL statement
    2. Transactions add variable overhead across frameworks
    3. Keeps comparison simpler and more consistent
    4. Frameworks have different transaction behaviors (some auto-commit, others need explicit commits)
  - **Production note:** Real systems coordinating DB writes + message queue publishes should use distributed transactions or the Outbox pattern for consistency
- ❌ No caching - No in-memory or query result caching
- ✅ Framework defaults for connection pooling

### Framework & Libraries

- ✅ Use industry-standard libraries (e.g., SQLAlchemy, GORM, Hibernate)
- ✅ Follow idiomatic patterns for each framework
- ✅ Use native libraries that support PostgreSQL & RabbitMQ

### Configuration

- ✅ Framework default configurations
- ✅ No custom tuning or optimization
- ✅ Prefetch count: 50 messages (Bun uses 1 due to framework restrictions)
- ✅ Manual message acknowledgment
- ✅ **All connection configuration via environment variables** (no hardcoded credentials)
  - Each service must include a `.env.example` file
  - Services must work with or without a `.env` file (sensible defaults)
  - Standard variables: `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `RABBITMQ_HOST`, `RABBITMQ_PORT`, etc.

**Goal:** Measure how each framework performs "out of the box" when used idiomatically. These implementations prioritize fair comparison over production-readiness

## Contributing

This is a reference implementation project. If you find issues or want to add a new language/framework:

1. Follow the reference service specification exactly
2. Use idiomatic patterns for the language/framework
3. Use framework defaults (no custom optimization)
4. Document any necessary deviations in the service README
5. Add appropriate build commands to `scripts/tools/testing/run-all-tests.sh`
6. Add cleanup commands to `scripts/tools/build/clean-binaries.sh`

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

This project was created to provide a fair, comprehensive comparison of modern programming languages and frameworks for technical decision makers. Special thanks to the open-source communities that make these amazing tools available.
