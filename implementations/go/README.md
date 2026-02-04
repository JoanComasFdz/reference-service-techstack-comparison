# Go Reference Service - Performance Test 2025

A barebones Go implementation of the reference service for the Performance Test 2025.

## Overview

This service demonstrates a simple Go application that:
- Subscribes to `instrument.status.changed` events from RabbitMQ
- Stores instrument status data in PostgreSQL
- Publishes `instrumentstatus.kpi.updated` events to RabbitMQ
- Provides an HTTP endpoint to query the latest status

## Features

- Standard library HTTP server with gorilla/mux for routing
- PostgreSQL database with lib/pq driver
- RabbitMQ messaging with official amqp091-go client
- CloudEvents v1.0 specification compliance with extensions
- Graceful shutdown handling

## Prerequisites

- Go 1.23 or higher
- Docker and Docker Compose (for RabbitMQ and PostgreSQL)
- Running infrastructure services (see main README.md)

## Installing Go

### Linux (Ubuntu/WSL) with Bash or Zsh

1. **Download Go 1.23.2:**
   ```bash
   wget https://go.dev/dl/go1.23.2.linux-amd64.tar.gz -O /tmp/go1.23.2.linux-amd64.tar.gz
   ```

2. **Extract to your home directory:**
   ```bash
   rm -rf ~/go && tar -C ~ -xzf /tmp/go1.23.2.linux-amd64.tar.gz
   ```

3. **Add Go to your PATH permanently:**

   **For Bash (Ubuntu default):**
   ```bash
   echo 'export PATH=$PATH:$HOME/go/bin' >> ~/.bashrc
   source ~/.bashrc
   ```

   **For Zsh:**
   ```bash
   echo 'export PATH=$PATH:$HOME/go/bin' >> ~/.zshrc
   source ~/.zshrc
   ```

4. **Verify installation:**
   ```bash
   go version
   # Should output: go version go1.23.2 linux/amd64
   ```

**Note:** If you see a warning `GOPATH set to GOROOT has no effect`, you can safely ignore it.

### Windows (PowerShell)

1. **Download the Windows installer:**
   - Visit https://go.dev/dl/
   - Download `go1.23.2.windows-amd64.msi`

2. **Run the installer:**
   - Double-click the downloaded `.msi` file
   - Follow the installation wizard
   - The installer will automatically add Go to your PATH

3. **Verify installation (open a new PowerShell window):**
   ```powershell
   go version
   # Should output: go version go1.23.2 windows/amd64
   ```

**Alternative - Manual Installation:**
```powershell
# Download Go
Invoke-WebRequest -Uri "https://go.dev/dl/go1.23.2.windows-amd64.zip" -OutFile "$env:TEMP\go1.23.2.windows-amd64.zip"

# Extract to C:\Go
Expand-Archive -Path "$env:TEMP\go1.23.2.windows-amd64.zip" -DestinationPath "C:\" -Force

# Add to PATH (permanent)
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\Go\bin", [EnvironmentVariableTarget]::User)

# Verify (restart PowerShell first)
go version
```

### Troubleshooting

**Problem: `command not found: go` after installation**

- **Linux/WSL:** Make sure you've sourced your shell config:
  ```bash
  # For Bash
  source ~/.bashrc

  # For Zsh
  source ~/.zshrc
  ```
  Or open a new terminal window.

- **Windows:** Restart PowerShell after installation.

**Problem: `GOPATH set to GOROOT has no effect` warning**

This is harmless and occurs when Go is installed in `~/go` (which is also the default GOPATH). You can ignore it or set a custom GOPATH:
```bash
# Add to ~/.bashrc or ~/.zshrc
export GOPATH=$HOME/go-workspace
export PATH=$PATH:$HOME/go/bin:$GOPATH/bin
```

## Configuration

The service uses environment variables with sensible defaults:

| Variable | Default | Description |
|----------|---------|-------------|
| SERVER_PORT | 8094 | HTTP server port |
| RABBITMQ_PREFETCH_COUNT | 50 | RabbitMQ prefetch count |
| DB_HOST | localhost | PostgreSQL host |
| DB_PORT | 5432 | PostgreSQL port |
| DB_USER | admin | PostgreSQL username |
| DB_PASSWORD | admin | PostgreSQL password |
| DB_NAME | go_db | PostgreSQL database name |
| RABBITMQ_HOST | localhost | RabbitMQ host |
| RABBITMQ_PORT | 5672 | RabbitMQ port |
| RABBITMQ_USER | admin | RabbitMQ username |
| RABBITMQ_PASSWORD | admin | RabbitMQ password |

**Database Setup**: The service uses a dedicated database `go_db`. Create it before running the service:

```bash
# Connect to PostgreSQL via Docker
docker exec -it performancetest-postgres psql -U admin -d postgres

# Create the database
CREATE DATABASE go_db;
```

The service will automatically create the `go_instrument_status` table on startup.

## Building

```bash
# Install dependencies
go mod download

# Build the application
go build -o goReferenceService .

# Or build with optimizations for production
go build -ldflags="-s -w" -o goReferenceService .
```

## Running

```bash
# Ensure infrastructure is running
cd ../..
docker-compose up -d

# Run the service from the implementations/go directory
cd implementations/go
./goReferenceService

# Or run directly with go run
go run .
```

## API Endpoints

### GET /kpi

Returns the latest instrument status record from the database.

**Response (200 OK):**
```json
{
  "id": 1,
  "deviceId": "device-123",
  "previousStatus": "idle",
  "currentStatus": "running",
  "timestamp": "2025-10-16T10:30:00Z"
}
```

If no records exist, returns an empty object:
```json
{}
```

## Events

### Subscribed Events

**Topic:** `instrument.status.changed`
**Queue:** `go`
**Exchange:** `referenceservice.comparison`

```json
{
  "id": "uuid",
  "specversion": "1.0",
  "source": "urn:uuid:device-simulator",
  "type": "instrument.status.changed",
  "time": "2025-10-16T10:30:00Z",
  "privacyrelevant": false,
  "datacontenttype": "application/json",
  "dataschema": "https://example.com/schemas/events-catalog/instrument.status.changed.schema.json",
  "kind": "event",
  "data": {
    "deviceId": "device-123",
    "previousStatus": "idle",
    "currentStatus": "running"
  }
}
```

Note: The `privacyrelevant` and `kind` fields are custom CloudEvents extensions used in this implementation.

### Published Events

**Topic:** `instrumentstatus.kpi.updated`
**Exchange:** `referenceservice.comparison`

```json
{
  "id": "uuid",
  "specversion": "1.0",
  "source": "urn:uuid:go-service",
  "type": "instrumentstatus.kpi.updated",
  "time": "2025-10-16T10:30:00Z",
  "privacyrelevant": false,
  "datacontenttype": "application/json",
  "dataschema": "https://example.com/schemas/events-catalog/instrumentstatus.kpi.updated.schema.json",
  "kind": "event",
  "data": {
    "deviceId": "device-123",
    "currentStatus": "running"
  }
}
```

Note: The `privacyrelevant` and `kind` fields are custom CloudEvents extensions used in this implementation.

## Performance Testing

To run performance tests:

```bash
# Start the service
./goReferenceService

# In another terminal, run the performance tester
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8094
```

## Project Structure

```
go/
├── main.go          # Application entry point
├── config.go        # Configuration management
├── server.go        # HTTP server and endpoints
├── database.go      # Database operations
├── rabbitmq.go      # RabbitMQ messaging
├── events.go        # Event models (CloudEvents)
├── go.mod           # Go module definition
└── README.md        # This file
```

## Dependencies

- `github.com/gorilla/mux` - HTTP router and URL matcher
- `gorm.io/driver/postgres` - PostgreSQL driver for GORM
- `gorm.io/gorm` - GORM ORM library
- `github.com/rabbitmq/amqp091-go` - RabbitMQ client
- `github.com/google/uuid` - UUID generation

## Notes

- This is a barebones implementation focused on framework performance
- Uses GORM for database operations with auto-migration
- Connection pooling handled by GORM defaults
- Follows CloudEvents v1.0 specification with custom extensions
- Uses manual acknowledgment for RabbitMQ messages (msg.Ack() on success, msg.Nack() on failure)
- Database table is created automatically on startup via GORM auto-migration
