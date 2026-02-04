# Rust Actix Reference Service

A reference implementation of the reference service built with Rust and Actix-web framework.

## Overview

This service demonstrates a barebones implementation using Rust and the Actix-web framework. It connects to RabbitMQ and PostgreSQL, processes events, and provides an HTTP API.

## Features

- **RabbitMQ Integration**: Subscribes to `instrument.status.changed` events and publishes `instrumentstatus.kpi.updated` events
- **PostgreSQL Database**: Stores instrument status data with automatic schema initialization
- **HTTP API**: Provides GET /kpi endpoint to retrieve the latest instrument status
- **CloudEvents Support**: Full support for CloudEvents v1.0 specification with custom extensions

## Prerequisites

- Rust 1.70+ (see main README.md for installation instructions)
- Docker Compose (for RabbitMQ and PostgreSQL infrastructure)
- Build tools: `build-essential`, `pkg-config`, `libssl-dev`

## Configuration

Copy the example environment file and modify as needed:

```bash
cp .env.example .env
```

Default configuration:
- **Database**: `postgresql://admin:admin@localhost:5432/rust_db`
- **RabbitMQ**: `amqp://admin:admin@localhost:5672`
- **Server**: `0.0.0.0:8100`
- **Queue Name**: `rustActix`
- **RabbitMQ Prefetch Count**: `50`

## Building

### Development Build

```bash
cargo build
```

### Release Build (Optimized)

```bash
cargo build --release
```

The release build includes optimizations:
- LTO (Link Time Optimization) enabled
- Single codegen unit for maximum optimization
- Optimization level 3

## Running

### Using Cargo (Development)

```bash
cargo run
```

### Using Cargo (Release)

```bash
cargo run --release
```

### Running the Binary Directly

After building in release mode:

```bash
./target/release/rustReferenceService
```

## Project Structure

```
rust/
├── src/
│   ├── main.rs          # Application entry point and service coordination
│   ├── api.rs           # HTTP API endpoints
│   ├── database.rs      # PostgreSQL connection and operations
│   ├── handlers.rs      # Event processing logic
│   ├── models.rs        # Data models and event structures
│   └── rabbitmq.rs      # RabbitMQ setup and messaging
├── Cargo.toml           # Project dependencies and configuration
├── .env.example         # Example environment variables
└── README.md            # This file
```

## API Endpoints

### GET /kpi

Returns the latest instrument status record from the database.

**Response (Success - 200 OK):**
```json
{
  "id": 123,
  "device_id": "device-001",
  "previous_status": "IDLE",
  "current_status": "RUNNING",
  "timestamp": "2025-10-16T12:00:00Z"
}
```

**Response (Not Found - 404):**
```json
{
  "error": "No instrument status records found"
}
```

## Performance Testing

1. Start the infrastructure:
```bash
docker-compose up -d
```

2. Start the service:
```bash
cargo run --release
```

3. Run the performance tester:
```bash
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8100
```

## Dependencies

- **actix-web**: Web framework for HTTP server
- **sea-orm**: Async ORM for Rust with PostgreSQL support
- **lapin**: Async RabbitMQ client (AMQP)
- **tokio**: Async runtime
- **serde/serde_json**: JSON serialization
- **chrono**: Date and time handling
- **uuid**: UUID generation for event IDs
- **log/env_logger**: Logging infrastructure
- **dotenv**: Environment variable management
- **anyhow**: Error handling

## Logging

Set the log level via the `RUST_LOG` environment variable:

```bash
# Info level (default)
RUST_LOG=info cargo run

# Debug level (verbose)
RUST_LOG=debug cargo run

# Specific module
RUST_LOG=rustReferenceService=debug cargo run
```

## Notes

- This is a barebones implementation for performance measurement purposes
- No additional optimizations (like custom connection pooling or caching) are included
- The service automatically creates the database schema on startup
- RabbitMQ exchange and queue are declared automatically
- Messages are acknowledged after successful processing or requeued on error
