# Performance Test - Java 21 Quarkus with GraalVM Native Image

This is the Java 21 Quarkus implementation of the performance test reference service with native image support using GraalVM. Quarkus is a Kubernetes-native Java framework optimized for GraalVM and OpenJDK HotSpot, offering fast startup times and low memory consumption.

## What is Quarkus?

**Quarkus** is a modern Java framework designed for cloud-native applications:
- **Container-first**: Optimized for running in containers with minimal resource usage
- **GraalVM Native Image**: Built-in support for compiling to native executables
- **Reactive and Imperative**: Supports both programming models
- **Developer Joy**: Live reload during development, unified configuration, and more
- **Standards-based**: Uses Jakarta EE, MicroProfile, and other standard APIs

### Benefits of Quarkus with GraalVM Native Image

- **Ultra-fast startup**: Applications start in ~50ms (vs. 2-5 seconds for traditional JVM)
- **Minimal memory footprint**: Native images consume 50-80% less memory than JVM
- **Cloud-native**: Perfect for microservices, serverless, and Kubernetes deployments
- **High density**: Run more instances on the same hardware
- **Simplified stack**: Fewer dependencies and smaller attack surface

### Trade-offs

- **Build time**: Native image compilation takes 2-5 minutes (vs. seconds for JAR)
- **Build resources**: Requires more memory and CPU during compilation
- **Reflection limitations**: Some dynamic Java features require explicit configuration

## Prerequisites

Before you start, ensure you have:

1. **Java 21** - Required for both regular and native builds
2. **GraalVM for Java 21** with Native Image tools
3. **Maven** - Build tool
4. **PostgreSQL** - Database (same as other performance test services)
5. **RabbitMQ** - Message broker (same as other performance test services)
6. **Native image build tools** - C compiler and build tools for your OS

### Install GraalVM (if not already installed)

Follow the installation instructions in the [main README](../README.md#install-graalvm-21-and-native-image).

Quick version for Linux/WSL2:

```bash
# Install SDKMAN if not already installed
curl -s "https://get.sdkman.io" | bash
source "$HOME/.sdkman/bin/sdkman-init.sh"

# Install GraalVM Java 21
sdk install java 21.0.2-graal

# Verify installation
java -version
native-image --version
```

### Install Native Build Tools

Native image compilation requires a C compiler and standard build tools.

**Linux / WSL2:**
```bash
sudo apt-get update
sudo apt-get install build-essential zlib1g-dev
```

**macOS:**
```bash
xcode-select --install
```

**Windows:**
Install Visual Studio 2022 with "Desktop development with C++" workload.

## Project Structure

```
java21quarkusgraal/
├── src/
│   └── main/
│       ├── java/
│       │   └── com/joancomasfdz/performancetest/
│       │       ├── InstrumentStatus.java            # JPA entity using Panache
│       │       ├── InstrumentStatusRepository.java  # Panache repository
│       │       ├── KpiEndpoint.java                 # REST endpoint
│       │       └── StatusChangedHandler.java        # RabbitMQ message handler
│       └── resources/
│           └── application.properties                # Application configuration
├── pom.xml                                          # Maven build configuration
└── README.md                                        # This file
```

## Key Features

1. **Queue Name**: Uses `javaQuarkusGraal` (to avoid conflicts)
2. **Server Port**: Runs on port `8096`
3. **Event Source**: Identifies as `urn:uuid:java21-quarkus-graal`
4. **Panache**: Uses Hibernate ORM with Panache for simplified database access
5. **Reactive Messaging**: Uses SmallRye Reactive Messaging for RabbitMQ
6. **Native Image**: Full support for GraalVM native image compilation

The business logic, database schema, and RabbitMQ integration match the Spring Boot reference implementation.

## Building the Service

First, ensure the shared `java21-events` library is installed:

```bash
# From the repository root
cd java21.events
mvn clean install
cd ../java21quarkusgraal
```

### Option 1: Build as Regular JAR (JVM)

Build and run like any Quarkus application:

```bash
# Build (produces a uber-jar)
mvn clean package

# Run
java -jar target/javaQuarkusReferenceService.jar
```

### Option 2: Build as Native Image (Recommended)

Build a native executable using GraalVM Native Image:

```bash
# Build native image (this will take 2-5 minutes)
mvn clean package -Dnative

# The native executable will be at:
# target/java21QuarkusGraalReferenceService-runner

# Run the native executable
./target/java21QuarkusGraalReferenceService-runner
```

**Note**: Native image compilation is resource-intensive and may require:
- 8GB+ RAM available
- Several minutes to complete
- Adequate disk space

### Build Time Comparison

- **Regular JAR**: ~10-30 seconds
- **Native Image**: ~2-5 minutes (depending on your machine)

## Running the Service

### Prerequisites

Ensure PostgreSQL and RabbitMQ are running:

```bash
# Using Docker Compose (from repository root)
cd ..
docker-compose up -d

# Verify services are running
docker ps
```

### Running the Native Executable

```bash
./target/java21QuarkusGraalReferenceService-runner
```

**Startup time**: ~50-100ms (vs. 2-5 seconds for JVM)

**Expected output:**
```
__  ____  __  _____   ___  __ ____  ______
 --/ __ \/ / / / _ | / _ \/ //_/ / / / __/
 -/ /_/ / /_/ / __ |/ , _/ ,< / /_/ /\ \
--\___\_\____/_/ |_/_/|_/_/|_|\____/___/
INFO  [io.quarkus] (main) java21QuarkusGraalReferenceService 1.0.0 native (powered by Quarkus 3.16.3) started in 0.052s
INFO  [io.quarkus] (main) Profile prod activated.
INFO  [io.quarkus] (main) Installed features: [cdi, hibernate-orm, hibernate-orm-panache, jdbc-postgresql, narayana-jta, rest, rest-jackson, smallrye-context-propagation, smallrye-reactive-messaging, smallrye-reactive-messaging-rabbitmq]
```

### Running the JAR

```bash
java -jar target/javaQuarkusReferenceService.jar
```

**Startup time**: ~1-2 seconds

### Development Mode (with Live Reload)

For development, Quarkus offers live reload:

```bash
mvn quarkus:dev
```

This will:
- Start the application in development mode
- Enable live reload (changes are reflected immediately)
- Run on port 8096

## Testing the Service

### 1. Check Health

```bash
# The service runs on port 8096
curl http://localhost:8096/kpi
```

### 2. Send a Test Event

Use the event simulator or publish directly to RabbitMQ. You can also use the performance-tester:

```bash
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8096
```

### 3. Verify Database

```bash
# Connect to PostgreSQL
docker exec -it performancetest-postgres psql -U admin -d performancetest_db

# Query instrument status
SELECT * FROM java21quarkusgraal_instrument_status;
```

## Configuration

Configuration is in `src/main/resources/application.properties`:

- **Server port**: 8096
- **Queue name**: `javaQuarkusGraal`
- **Database**: `performancetest_db` (same as other services)
- **RabbitMQ**: localhost:5672 (same as other services)
- **Exchange**: `referenceservice.comparison`

You can override configuration at runtime:

```bash
# Override port
./target/java21QuarkusGraalReferenceService-runner -Dquarkus.http.port=8081

# Override database
./target/java21QuarkusGraalReferenceService-runner -Dquarkus.datasource.jdbc.url=jdbc:postgresql://myhost:5432/mydb
```

## Performance Comparison

### Startup Time
- **JVM**: 1-2 seconds
- **Native**: 50-100 milliseconds (~15-20x faster)

### Memory Footprint (RSS)
- **JVM**: ~150-250 MB
- **Native**: ~50-80 MB (~3x less)

### First Request Latency
- **JVM**: Higher initially, optimizes over time with JIT
- **Native**: Consistent low latency from the start

## Troubleshooting

### Build Fails: "native-image: command not found"

**Solution**: Ensure GraalVM is installed and `native-image` is available:
```bash
native-image --version
```

### Build Fails: Missing C compiler

**Solution**: Install build tools as described in "Installing Native Build Tools" section.

### Application Fails to Connect to Database/RabbitMQ

**Solution**: Ensure services are running:
```bash
docker-compose up -d
docker ps  # Verify both PostgreSQL and RabbitMQ are running
```

### Out of Memory During Build

**Solution**: Increase Maven memory:
```bash
export MAVEN_OPTS="-Xmx8g"
mvn clean package -Dnative
```

### Native Image Reflection Errors

**Solution**: Quarkus typically handles reflection configuration automatically. If you encounter issues, check the Quarkus logs for guidance.

## Performance Testing

To test this service with the performance tester:

```bash
# 1. Start infrastructure
docker-compose up -d

# 2. Build and run the service
cd java21quarkusgraal
mvn clean package -Dnative
./target/java21QuarkusGraalReferenceService-runner

# 3. In a new terminal, run the performance tester
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8096
```

The performance tester will generate reports comparing this service with other implementations.

## Monitoring Native vs JVM Performance

To compare performance:

```bash
# Terminal 1 - JVM version
mvn clean package
java -jar target/javaQuarkusReferenceService.jar

# Terminal 2 - Native version (in a new session)
mvn clean package -Dnative
./target/java21QuarkusGraalReferenceService-runner

# Monitor with htop or Task Manager to see memory usage
# Use 'time' command to measure startup time
```

## Additional Resources

- [Quarkus Documentation](https://quarkus.io/guides/)
- [Quarkus Native Image Guide](https://quarkus.io/guides/building-native-image)
- [GraalVM Documentation](https://www.graalvm.org/latest/docs/)
- [SmallRye Reactive Messaging](https://smallrye.io/smallrye-reactive-messaging/)
- [Hibernate ORM with Panache](https://quarkus.io/guides/hibernate-orm-panache)

## Summary

This Quarkus + GraalVM native image implementation demonstrates:
- ✅ Ultra-fast startup times (~50ms) ideal for cloud-native deployments
- ✅ Minimal memory footprint (~50-80MB) for better resource efficiency
- ✅ Same functionality as Spring Boot implementations
- ✅ Modern reactive programming with SmallRye Reactive Messaging
- ✅ Simplified persistence with Hibernate ORM Panache
- ✅ Developer-friendly with live reload and unified configuration

The combination of Quarkus and GraalVM Native Image provides the best balance of developer experience and runtime performance for cloud-native Java applications.
