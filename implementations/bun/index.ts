import * as amqp from "amqplib";
import { PrismaClient } from "./generated/prisma";

// Configuration from environment variables
const RABBITMQ_HOST = process.env.RABBITMQ_HOST || "localhost";
const RABBITMQ_PORT = parseInt(process.env.RABBITMQ_PORT || "5672", 10);
const RABBITMQ_USER = process.env.RABBITMQ_USER || "admin";
const RABBITMQ_PASSWORD = process.env.RABBITMQ_PASSWORD || "admin";
const RABBITMQ_PREFETCH_COUNT = parseInt(process.env.RABBITMQ_PREFETCH_COUNT || "1", 10);
const EXCHANGE_NAME = process.env.EXCHANGE_NAME || "referenceservice.comparison";
const QUEUE_NAME = process.env.QUEUE_NAME || "bun";
const ROUTING_KEY_STATUS_CHANGED = process.env.ROUTING_KEY_STATUS_CHANGED || "instrument.status.changed";
const ROUTING_KEY_KPI_UPDATED = process.env.ROUTING_KEY_KPI_UPDATED || "instrumentstatus.kpi.updated";

const HTTP_PORT = parseInt(process.env.HTTP_PORT || "8090", 10);

// Event type constants
const EVENT_TYPE_KPI_UPDATED = "instrumentstatus.kpi.updated";

// Spec version constants
const SPEC_VERSION = "1.0";

// URN constants
const SOURCE_URN = "urn:uuid:bun";
const DATA_SCHEMA_KPI_UPDATED = "https://example.com/schemas/events-catalog/instrumentstatus.kpi.updated.schema.json";

// CloudEvents interfaces
interface CloudEvent {
  id: string;
  specversion: string;
  source: string;
  type: string;
  time: string;
  privacyrelevant: boolean;
  datacontenttype: string;
  dataschema: string;
  kind: string;
  data: Record<string, any>;
}

interface InstrumentStatusChangedEvent extends CloudEvent {
  data: {
    deviceId: string;
    previousStatus: string;
    currentStatus: string;
  };
}

interface InstrumentStatusKpiUpdatedEvent extends CloudEvent {
  data: {
    deviceId: string;
    currentStatus: string;
  };
}

// Prisma Client instance
const prisma = new PrismaClient();

// HTTP server instance for graceful shutdown
let httpServer: ReturnType<typeof Bun.serve> | null = null;

// RabbitMQ setup
async function initializeRabbitMQ(): Promise<amqp.ConfirmChannel> {
  console.log("Connecting to RabbitMQ...");
  const connection = await amqp.connect({
    protocol: "amqp",
    hostname: RABBITMQ_HOST,
    port: RABBITMQ_PORT,
    username: RABBITMQ_USER,
    password: RABBITMQ_PASSWORD,
  });

  const channel = await connection.createConfirmChannel();

  // Declare exchange
  await channel.assertExchange(EXCHANGE_NAME, "topic", {
    durable: true,
    autoDelete: false,
  });

  // Declare queue
  await channel.assertQueue(QUEUE_NAME, {
    durable: true,
    autoDelete: false,
  });

  // Bind queue to exchange
  await channel.bindQueue(QUEUE_NAME, EXCHANGE_NAME, ROUTING_KEY_STATUS_CHANGED);

  // ⚠️ IMPORTANT: prefetch MUST be 1 (not 50 like other services) ⚠️
  // amqplib fires callbacks for ALL prefetched messages immediately (parallel processing).
  // Unlike Go/Rust iterator patterns, JavaScript requires prefetch(1) for sequential processing.
  // This adds network latency vs other services - see README for full explanation.
  await channel.prefetch(RABBITMQ_PREFETCH_COUNT);

  console.log(`RabbitMQ initialized: Exchange=${EXCHANGE_NAME}, Queue=${QUEUE_NAME}, Prefetch=${RABBITMQ_PREFETCH_COUNT}`);

  return channel;
}

// Create KPI event
function createKpiEvent(deviceId: string, currentStatus: string): InstrumentStatusKpiUpdatedEvent {
  return {
    id: crypto.randomUUID(),
    specversion: SPEC_VERSION,
    source: SOURCE_URN,
    type: EVENT_TYPE_KPI_UPDATED,
    time: new Date().toISOString(),
    privacyrelevant: false,
    datacontenttype: "application/json",
    dataschema: DATA_SCHEMA_KPI_UPDATED,
    kind: "event",
    data: {
      deviceId,
      currentStatus,
    },
  };
}

// Publish KPI event
async function publishKpiEvent(channel: amqp.ConfirmChannel, deviceId: string, currentStatus: string): Promise<void> {
  const kpiEvent = createKpiEvent(deviceId, currentStatus);
  const messageBuffer = Buffer.from(JSON.stringify(kpiEvent));

  channel.publish(EXCHANGE_NAME, ROUTING_KEY_KPI_UPDATED, messageBuffer, {
    contentType: "application/json",
  });

  // Wait for RabbitMQ to confirm the message was received
  await channel.waitForConfirms();

  console.log(`Published KPI event for device: ${deviceId}`);
}

// Process incoming message
async function processMessage(channel: amqp.ConfirmChannel, message: amqp.ConsumeMessage): Promise<void> {
  const messageContent = message.content.toString();
  const event: InstrumentStatusChangedEvent = JSON.parse(messageContent);

  console.log(`Received event: ${event.type} from ${event.source}`);

  const { deviceId, previousStatus, currentStatus } = event.data;

  console.log(`Processing status change for device: ${deviceId} from ${previousStatus} to ${currentStatus}`);

  // Save to database using Prisma
  const savedStatus = await prisma.bunInstrumentStatus.create({
    data: {
      deviceId,
      previousStatus,
      currentStatus,
      timestamp: new Date(),
    },
  });

  console.log(`Saved instrument status to database with ID: ${savedStatus.id}`);

  // Publish KPI event (waits for confirmation)
  await publishKpiEvent(channel, deviceId, currentStatus);
}

// Start consuming messages
async function startConsumer(channel: amqp.ConfirmChannel): Promise<void> {
  console.log(`Starting consumer for queue: ${QUEUE_NAME}`);

  await channel.consume(
    QUEUE_NAME,
    async (message) => {
      if (message) {
        try {
          await processMessage(channel, message);
          channel.ack(message);
        } catch (error) {
          console.error("Error processing message:", error);
          // Reject and requeue the message on failure
          channel.nack(message, false, true);
        }
      }
    },
    { noAck: false }
  );

  console.log(`Consumer started for queue: ${QUEUE_NAME}`);
}

// HTTP server
async function startHttpServer(): Promise<void> {
  httpServer = Bun.serve({
    port: HTTP_PORT,
    async fetch(req) {
      const url = new URL(req.url);

      if (url.pathname === "/kpi" && req.method === "GET") {
        try {
          console.log("GET /kpi - Fetching latest instrument status");

          // Fetch latest status using Prisma
          const result = await prisma.bunInstrumentStatus.findFirst({
            orderBy: {
              timestamp: 'desc',
            },
          });

          if (result) {
            return new Response(JSON.stringify(result), {
              headers: { "Content-Type": "application/json" },
            });
          } else {
            return new Response(JSON.stringify({}), {
              headers: { "Content-Type": "application/json" },
            });
          }
        } catch (error) {
          console.error("Error fetching KPI:", error);
          return new Response(JSON.stringify({ error: "Internal server error" }), {
            status: 500,
            headers: { "Content-Type": "application/json" },
          });
        }
      }

      return new Response("Not Found", { status: 404 });
    },
  });

  console.log(`HTTP server listening on port ${HTTP_PORT}`);
}

// Ensure database schema exists using raw SQL
//
// ⚠️ WHY RAW SQL INSTEAD OF IDIOMATIC `prisma db push`?
//
// The idiomatic Prisma approach would be to run `prisma db push` programmatically via execSync:
//   execSync("bunx prisma db push --skip-generate --accept-data-loss", {...})
//
// However, this approach has a critical limitation when called from within a Node.js/Bun application:
// - The Prisma CLI process enters an uninterruptible sleep state (Linux "D" state)
// - This happens regardless of stdio options (inherit, pipe, ignore)
// - Affects both `bunx prisma` and `npx prisma`
// - The service startup hangs indefinitely waiting for Prisma to complete
//
// This is a known limitation with running CLI tools programmatically via child_process.
// The CLI works perfectly when run manually from the command line, but not from execSync.
//
// ALTERNATIVES CONSIDERED:
// 1. External script wrapper - Requires users to run `bunx prisma db push` before starting service
//    ❌ Defeats the purpose of code-first automatic database setup
//
// 2. Shell script wrapper - Works but adds platform-specific complexity (.sh vs .bat)
//    ❌ Less portable, more complex for a reference service
//
// 3. Raw SQL (current approach) - Directly creates schema using Prisma Client
//    ✅ Reliable and works on all platforms
//    ✅ Maintains code-first approach (automatic on startup)
//    ✅ No subprocess issues
//    ⚠️  Schema defined in two places (schema.prisma + raw SQL below)
//
// DECISION: Use raw SQL for reliability and simplicity in a reference service context.
// The schema.prisma file remains the source of truth for Prisma Client type generation.
async function ensureSchema(): Promise<void> {
  try {
    console.log("Ensuring database schema exists...");

    // Create table if it doesn't exist
    // Note: This mirrors the schema defined in prisma/schema.prisma
    await prisma.$executeRawUnsafe(`
      CREATE TABLE IF NOT EXISTS bun_instrument_status (
        id SERIAL PRIMARY KEY,
        device_id VARCHAR(255) NOT NULL,
        previous_status VARCHAR(255) NOT NULL,
        current_status VARCHAR(255) NOT NULL,
        timestamp TIMESTAMPTZ NOT NULL
      )
    `);

    // Create index if it doesn't exist
    // Note: This mirrors the index defined in prisma/schema.prisma
    await prisma.$executeRawUnsafe(`
      CREATE INDEX IF NOT EXISTS idx_bun_instrument_status_timestamp
      ON bun_instrument_status (timestamp DESC)
    `);

    console.log("Database schema verified");
  } catch (error) {
    console.error("Error ensuring schema:", error);
    throw error;
  }
}

// Main startup
async function main(): Promise<void> {
  try {
    console.log("Starting Bun Reference Service...");

    // Ensure schema exists (Prisma Client connects lazily on first query)
    await ensureSchema();

    // Initialize RabbitMQ
    const channel = await initializeRabbitMQ();

    // Start consumer
    await startConsumer(channel);

    // Start HTTP server
    await startHttpServer();

    console.log("Bun Reference Service started successfully");

    // Graceful shutdown
    process.on('SIGINT', async () => {
      console.log('Shutting down...');
      if (httpServer) {
        httpServer.stop();
        console.log('HTTP server stopped');
      }
      await prisma.$disconnect();
      process.exit(0);
    });

  } catch (error) {
    console.error("Error starting service:", error);
    await prisma.$disconnect();
    process.exit(1);
  }
}

main();
