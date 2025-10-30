# Java 25 Spring Boot Reference Service

Java 25 Spring Boot Reference Service - a barebones implementation using Java 25 and Spring Boot 3.x for the Performance Test.

## Prerequisites

- Java 25 JDK
- Maven 3.6+
- RabbitMQ (via docker-compose in parent directory)
- PostgreSQL (via docker-compose in parent directory)
- `java25.events` library (built from `../java25.events`)

## Database Setup

The service uses a dedicated database `java25springboot_db`. Create it before running the service:

```bash
# Connect to PostgreSQL via Docker
docker exec -it performancetest-postgres psql -U admin -d postgres

# Create the database
CREATE DATABASE java25springboot_db;
```

The service will automatically create/update tables using Hibernate's `ddl-auto: update` strategy.

## Build

First, build the shared `java25.events` library:

```bash
cd ../java25.events
mvn clean package
cd ../java25springboot
```

Then build the Spring Boot service:

```bash
mvn clean package
```

## Run

### Using Maven
```bash
mvn spring-boot:run
```

### Using JAR
```bash
java -jar target/javaSpringBootReferenceService25.jar
```

## Debug

### VS Code
1. Open the `java25springboot` folder in VS Code
2. Install the "Extension Pack for Java" extension
3. Press F5 or use the Run and Debug panel
4. Select "Debug Spring Boot App"

### IntelliJ IDEA
1. Open the `java25springboot` folder as a Maven project
2. Right-click on `Java25SpringBootReferenceServiceApplication.java`
3. Select "Debug 'Java25SpringBootReferenceServiceApplication.main()'"

## API Endpoints

- `GET http://localhost:8101/kpi` - Returns the latest instrument status record from the database (not all records)

## Functionality

1. **On Startup**: Creates exchange `referenceservice.comparison` and queue `javaSB` in RabbitMQ, binds them together
2. **Listens**: Subscribes to `instrument.status.changed` events from RabbitMQ
3. **Stores**: Saves event data to PostgreSQL database
4. **Publishes**: Sends `instrumentstatus.kpi.updated` events to RabbitMQ
5. **API**: Provides `/kpi` endpoint to retrieve the latest stored record

## Measurements

### Startup Time
```bash
# Measure time from start to "Started Java25SpringBootReferenceServiceApplication"
time mvn spring-boot:run
# Or with JAR:
time java -jar target/javaSpringBootReferenceService25.jar
```

### Memory Consumption
```bash
# Start the application
java -jar target/javaSpringBootReferenceService25.jar &
APP_PID=$!

# Check memory at start (after "Started Java25SpringBootReferenceServiceApplication" log)
ps -o pid,rss,vsz,cmd -p $APP_PID

# Or use jps and jcmd for more detailed metrics
jps
jcmd <PID> VM.native_memory summary

# Send events using Python script from parent directory
cd ..
python send_events.py -n 10

# Call the /kpi endpoint
curl http://localhost:8101/kpi

# Check memory again after processing
ps -o pid,rss,vsz,cmd -p $APP_PID

# Kill the app
kill $APP_PID
```

### Alternative: Use jstat for GC metrics
```bash
jstat -gc <PID> 1000
```

## Architecture

The service follows vertical slice architecture with minimal folders:

- `Java25SpringBootReferenceServiceApplication.java` - Main entry point
- `RabbitConfig.java` - RabbitMQ configuration
- `InstrumentStatus.java` - JPA entity and repository
- `StatusChangedHandler.java` - Event listener, processor, and KPI publisher
- `KpiEndpoint.java` - REST controller

All components are in the same package to minimize complexity.

### Dependencies

This service uses the shared `java25.events` library for CloudEvents v1.0:
- **Library**: `com.joancomasfdz.java25.events:java25-events:1.0.0`
- **Location**: `../java25.events`
- **Class**: `com.joancomasfdz.java25.events.CloudEvent`

The event classes are a framework-agnostic POJO that can be reused across all performance test services (Spring Boot, Quarkus, etc.).
