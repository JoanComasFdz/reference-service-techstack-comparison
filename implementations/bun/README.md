# Bun Reference Service

A reference implementation of the Performance Test service using Bun.js.

## Important Performance Note: Sequential Processing Limitation

### Why This Service Uses prefetch(1) Instead of prefetch(50)

This implementation uses `prefetch(1)` instead of `prefetch(50)` like the other reference services (Go, Rust, .NET, Java). This is a **fundamental limitation of JavaScript AMQP client libraries**, not a performance choice.

#### The Problem

**Go/Rust Services:**
- Use iterator patterns (`for msg := range msgs` in Go, `while let Some(msg)` in Rust)
- These patterns naturally serialize message processing
- Can use `prefetch(50)` AND still process messages sequentially
- RabbitMQ sends 50 messages in bulk → low network latency

**JavaScript/amqplib Services:**
- Use callback-based `channel.consume()` API
- Callbacks are **fired immediately for all prefetched messages**
- With `prefetch(50)`, all 50 messages trigger callbacks concurrently
- This results in **parallel processing** (up to 50 messages at once), not sequential

#### The Solution

Setting `prefetch(1)` ensures RabbitMQ only sends the next message after the current one is acknowledged. This enforces sequential processing but adds network latency due to more round trips.

#### Performance Implications

When comparing performance metrics between services:
- **Bun (prefetch=1)**: Higher network latency per message due to round-trip overhead
- **Go/Rust/etc (prefetch=50)**: Lower network latency due to bulk message delivery

This creates an inherent disadvantage for JavaScript implementations when doing truly sequential message processing.

#### References

- [amqplib Issue #495](https://github.com/amqp-node/amqplib/issues/495) - Discussion on async callbacks
- [amqplib Issue #662](https://github.com/amqp-node/amqplib/issues/662) - Maintainer explanation of design decision
- [RabbitMQ Tutorial](https://www.rabbitmq.com/tutorials/tutorial-two-javascript) - Prefetch documentation

Note: Modern alternatives like `@cloudamqp/amqp-client` still use callback-based patterns without native async iterator support. This limitation appears to be fundamental to JavaScript AMQP clients.

## Schema Management Approach: Why Raw SQL Instead of `prisma db push`

### The Idiomatic Approach (Doesn't Work)

The idiomatic Prisma approach for code-first database setup would be to run `prisma db push` programmatically on service startup:

```typescript
import { execSync } from "child_process";

// Attempt to run Prisma CLI programmatically
execSync("bunx prisma db push --skip-generate --accept-data-loss", {
  env: process.env,
  encoding: "utf-8",
});
```

### The Problem

This approach has a **critical limitation** when called from within a Node.js/Bun application:

- The Prisma CLI process enters an **uninterruptible sleep state** (Linux "D" state)
- Service startup **hangs indefinitely** waiting for Prisma to complete
- This happens regardless of:
  - stdio options (`inherit`, `pipe`, `ignore`)
  - Using `bunx prisma` vs `npx prisma`
  - Using `execSync` vs `spawn`

The Prisma CLI works perfectly when run manually from the command line, but not when called via `child_process` from within an application. This appears to be a known limitation with running CLI tools programmatically.

### Alternatives Considered

1. **External Script Wrapper** - Require users to run `bunx prisma db push` before starting the service
   - ❌ Defeats the purpose of code-first automatic database setup
   - ❌ Extra manual step

2. **Shell Script Wrapper** - Create a startup script that runs both commands
   - ❌ Platform-specific (.sh vs .bat)
   - ❌ Adds complexity for a reference service

3. **Raw SQL (Current Approach)** - Use `$executeRawUnsafe` to create schema
   - ✅ Reliable and works on all platforms
   - ✅ Maintains code-first approach (automatic on startup)
   - ✅ No subprocess issues
   - ⚠️  Schema defined in two places (`schema.prisma` + raw SQL in code)

### Current Implementation

The service uses **raw SQL** via Prisma Client's `$executeRawUnsafe` for automatic schema creation:

```typescript
await prisma.$executeRawUnsafe(`
  CREATE TABLE IF NOT EXISTS bun_instrument_status (...)
`);
```

**Important Notes:**
- The `schema.prisma` file remains the **source of truth** for Prisma Client type generation
- The raw SQL **mirrors** the schema defined in `prisma/schema.prisma`
- If you modify the schema, you must update **both** the `schema.prisma` file and the raw SQL
- This trade-off prioritizes **reliability and simplicity** for a reference service

### Manual Schema Sync (Optional)

If you prefer to use Prisma's idiomatic approach, you can manually run:

```bash
bunx prisma db push --skip-generate
```

This will sync the database schema from `schema.prisma` before starting the service. However, the service will still run the raw SQL on startup (which is idempotent and safe).

## Prerequisites

- Bun installed (https://bun.sh)
- Docker and docker-compose (for RabbitMQ and PostgreSQL)

## Install Bun

If Bun is not installed, install it using:

```bash
# Linux/macOS
curl -fsSL https://bun.sh/install | bash

# Or using npm
npm install -g bun
```

## Environment Configuration

The service uses environment variables for configuration. You can either:

1. **Copy the example file and customize it:**
```bash
cp .env.example .env
# Edit .env with your specific values
```

2. **Use default values** - The service will work out of the box with sensible defaults matching the docker-compose infrastructure setup.

### Available Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATABASE_URL` | `postgresql://admin:admin@localhost:5432/bun_db?schema=public` | PostgreSQL connection string |
| `RABBITMQ_HOST` | `localhost` | RabbitMQ server hostname |
| `RABBITMQ_PORT` | `5672` | RabbitMQ server port |
| `RABBITMQ_USER` | `admin` | RabbitMQ username |
| `RABBITMQ_PASSWORD` | `admin` | RabbitMQ password |
| `EXCHANGE_NAME` | `referenceservice.comparison` | RabbitMQ exchange name |
| `QUEUE_NAME` | `bun` | RabbitMQ queue name |
| `ROUTING_KEY_STATUS_CHANGED` | `instrument.status.changed` | Routing key for incoming events |
| `ROUTING_KEY_KPI_UPDATED` | `instrumentstatus.kpi.updated` | Routing key for published events |
| `RABBITMQ_PREFETCH_COUNT` | `1` | RabbitMQ prefetch count (set to 1 due to JavaScript callback limitations) |
| `HTTP_PORT` | `8090` | HTTP server port |

## Setup

1. Start the required infrastructure:

```bash
# From the project root directory
cd ../..
docker-compose up -d
```

2. Install dependencies:

```bash
cd bun
bun install
```

**Current dependency versions (as of October 2025):**
- `@prisma/client` & `prisma`: 6.18.0
- `amqplib`: 0.10.9 (RabbitMQ 4.1+ compatible)
- `typescript`: 5.9.3
- `bun-types`: latest

## Build the Service

To build the standalone executable:

```bash
bun run build
```

This will create a compiled executable named `bunReferenceService` that can be run directly without requiring the Bun runtime.

## Run the Service

### Using the compiled executable (recommended for testing):

```bash
./bunReferenceService
```

### Using Bun runtime directly:

```bash
bun start
```

Or with auto-reload during development:

```bash
bun dev
```

The service will:
- Automatically create database schema using raw SQL (code-first approach)
  - Schema defined in `prisma/schema.prisma` for Prisma Client type generation
  - Schema created via `$executeRawUnsafe` for reliable automatic setup
  - See "Schema Management Approach" section below for details
- Connect to RabbitMQ and set up exchange/queue bindings
- Start consuming messages from the `bun` queue
- Start HTTP server on port 8090

## API Endpoints

- `GET /kpi` - Returns the latest instrument status record from the database

## Queue Configuration

- **Exchange**: `referenceservice.comparison`
- **Queue**: `bun`
- **Routing Keys**:
  - Subscribed: `instrument.status.changed`
  - Published: `instrumentstatus.kpi.updated`

## Testing

Run the performance test:

```bash
# From the implementations/bun directory
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8090
```

## Kill Service

If you need to stop the service running on port 8090:

```bash
kill -9 $(lsof -ti:8090)
```

Or find by process name:

```bash
pkill bunReferenceService
```
