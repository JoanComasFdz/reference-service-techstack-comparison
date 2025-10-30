# Performance Test - Java 21 Spring Boot with GraalVM Native Image

This is a GraalVM-enabled version of the Java 21 Spring Boot reference service. It provides the same functionality as the standard JVM version (`java21springboot`) but can be compiled to a native executable using GraalVM Native Image.

## What is GraalVM?

**GraalVM** is a high-performance JDK distribution that includes:
- A just-in-time (JIT) compiler that can improve application performance
- **Native Image**: A technology that compiles Java applications ahead-of-time into standalone native executables

### Benefits of GraalVM Native Image

- **Instant startup**: Applications start in milliseconds (vs. seconds for traditional JVM)
- **Low memory footprint**: Native images consume significantly less memory
- **Optimized performance**: Ahead-of-time compilation produces highly optimized executables
- **No JVM required**: The executable is standalone and doesn't need a JVM to run
- **Reduced attack surface**: Smaller runtime means fewer potential vulnerabilities

### Trade-offs

- **Build time**: Native image compilation takes longer than traditional JAR packaging
- **Build resources**: Requires more memory and CPU during compilation
- **Dynamic features**: Some Java dynamic features (reflection, dynamic class loading) require explicit configuration

## Prerequisites

Before you start, ensure you have:

1. **Java 21** - Required for both regular and native builds
2. **GraalVM for Java 21** (or GraalVM Native Image tools)
3. **Maven** - Build tool
4. **PostgreSQL** - Database (same as other performance test services)
5. **RabbitMQ** - Message broker (same as other performance test services)
6. **Native image build tools** - C compiler and build tools for your OS

## GraalVM Installation

### Option 1: Install Full GraalVM Distribution (Recommended)

#### On Linux / WSL2

```bash
# Download GraalVM for Java 21
# Visit https://www.graalvm.org/downloads/ to get the latest version
# Or use SDKMAN (recommended):

# Install SDKMAN if not already installed
curl -s "https://get.sdkman.io" | bash

# Make init script executable and configure timeouts
chmod +x ~/.sdkman/bin/sdkman-init.sh
sed -i 's/sdkman_curl_connect_timeout=.*/sdkman_curl_connect_timeout=30/' ~/.sdkman/etc/config
sed -i 's/sdkman_curl_max_time=.*/sdkman_curl_max_time=50/' ~/.sdkman/etc/config

# Load SDKMAN
source "$HOME/.sdkman/bin/sdkman-init.sh"

# Install GraalVM Java 21
sdk install java 21.0.2-graal

# Verify installation
java -version
# Should show: ... GraalVM ...

# Verify native-image is available
native-image --version
```

#### On macOS

```bash
# Using Homebrew
brew install --cask graalvm-jdk

# Or using SDKMAN (recommended - same as Linux)
curl -s "https://get.sdkman.io" | bash
chmod +x ~/.sdkman/bin/sdkman-init.sh
sed -i '' 's/sdkman_curl_connect_timeout=.*/sdkman_curl_connect_timeout=30/' ~/.sdkman/etc/config
sed -i '' 's/sdkman_curl_max_time=.*/sdkman_curl_max_time=50/' ~/.sdkman/etc/config
source "$HOME/.sdkman/bin/sdkman-init.sh"
sdk install java 21.0.2-graal

# Verify installation
java -version
native-image --version
```

#### On Windows

```powershell
# Download from https://www.graalvm.org/downloads/
# Extract the archive to a directory (e.g., C:\graalvm)

# Set JAVA_HOME environment variable
setx JAVA_HOME "C:\graalvm\graalvm-jdk-21"

# Add to PATH
setx PATH "%PATH%;%JAVA_HOME%\bin"

# Verify (in new terminal)
java -version
native-image --version
```

### Option 2: Add Native Image to Existing Java 21 JDK

If you already have a GraalVM JDK (even if it came with your Java installation):

```bash
# Using gu (GraalVM Updater) - if available
gu install native-image

# Or, Maven will automatically download the native-image-maven-plugin
# when you build with the native profile (see build instructions below)
```

### Installing Native Build Tools

Native image compilation requires a C compiler and standard build tools.

#### Linux / WSL2

```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install build-essential zlib1g-dev

# RHEL/CentOS/Fedora
sudo yum install gcc glibc-devel zlib-devel
```

#### macOS

```bash
# Install Xcode Command Line Tools
xcode-select --install
```

#### Windows

Install Visual Studio 2022 (or 2019) with "Desktop development with C++" workload, or install the Windows SDK.

**Important for Windows**: Native image builds must be run from a Visual Studio Developer Command Prompt.

## Project Structure

```
java21springbootgraal/
├── src/
│   └── main/
│       ├── java/
│       │   └── com/joancomasfdz/performancetest/
│       │       ├── Java21SpringBootGraalReferenceServiceApplication.java      # Main application
│       │       ├── RabbitConfig.java             # RabbitMQ configuration
│       │       ├── InstrumentStatus.java         # JPA entity
│       │       ├── KpiEndpoint.java              # REST endpoint
│       │       ├── StatusChangedHandler.java     # RabbitMQ message handler
│       │       └── NativeHintsConfig.java        # GraalVM reflection hints
│       └── resources/
│           └── application.yml                    # Application configuration
├── pom.xml                                        # Maven build configuration
└── README.md                                      # This file
```

## Key Differences from Standard Version

1. **Queue Name**: Uses `javaSBGraal` instead of `javaSB` (to avoid conflicts)
2. **Server Port**: Runs on port `8098` (assigned port for this implementation)
3. **Event Source**: Identifies as `urn:uuid:java21-springboot-graal`
4. **GraalVM Configuration**:
   - Added `NativeHintsConfig.java` for reflection hints
   - Modified `pom.xml` with native-maven-plugin
   - Native build profile in pom.xml

The business logic, database schema, and RabbitMQ integration remain identical.

## Building the Service

First, ensure the shared `java21.events` library is installed:

```bash
# From the repository root
cd java21.events
mvn clean install
cd ../java21springbootgraal
```

### Option 1: Build as Regular JAR (JVM)

Build and run like any Spring Boot application:

```bash
# Build
mvn clean package

# Run
java -jar target/javaSpringBootGraalReferenceService21.jar
```

### Option 2: Build as Native Image (Recommended)

Build a native executable using GraalVM Native Image:

```bash
# Build native image (this will take 5-10 minutes)
mvn clean package -Pnative

# The native executable will be at:
# target/javaSpringBootGraalReferenceService21

# Run the native executable
./target/javaSpringBootGraalReferenceService21
```

**Note**: Native image compilation is resource-intensive and may require:
- 8GB+ RAM available
- Several minutes to complete
- Adequate disk space

### Build Time Comparison

- **Regular JAR**: ~10-30 seconds
- **Native Image**: ~5-10 minutes (depending on your machine)

## Running the Service

### Prerequisites

Ensure PostgreSQL and RabbitMQ are running:

```bash
# Using Docker Compose (from repository root)
docker-compose up -d postgres rabbitmq

# Or start them individually if installed locally
```

### Running the Native Executable

```bash
./target/javaSpringBootGraalReferenceService21
```

**Startup time**: ~50-200ms (vs. 2-5 seconds for JVM)

### Running the JAR

```bash
java -jar target/javaSpringBootGraalReferenceService21.jar
```

**Startup time**: ~2-5 seconds

## Testing the Service

### 1. Check Health

```bash
# The service runs on port 8098
curl http://localhost:8098/kpi
```

### 2. Send a Test Event

Use the event simulator or publish directly to RabbitMQ:

```bash
# Install the shared library and use the simulator
cd ../event-simulator
# Follow simulator instructions to send events
```

### 3. Verify Database

```bash
# Connect to PostgreSQL
docker exec -it performancetest-postgres psql -U admin -d performancetest_db

# Query instrument status
SELECT * FROM instrument_status;
```

## Configuration

Configuration is in `src/main/resources/application.yml`:

- **Server port**: 8098 (assigned port for this implementation in the comparison suite)
- **Queue name**: `javaSBGraal` (unique queue name for this implementation)
- **Database**: `java21springbootgraal_db` (dedicated database for this service)
- **RabbitMQ**: Shared RabbitMQ instance with other services

## Performance Comparison

### Startup Time
- **JVM**: 2-5 seconds
- **Native**: 50-200 milliseconds (~20-40x faster)

### Memory Footprint
- **JVM**: ~150-300 MB
- **Native**: ~50-100 MB (~3x less)

### Runtime Performance
- **JVM**: Optimizes over time with JIT compilation
- **Native**: Consistent performance from the start

## Troubleshooting

### Build Fails: "native-image: command not found"

**Solution**: Ensure GraalVM is installed and `native-image` is available:
```bash
native-image --version
```

If not found, install as described in the installation section above.

### Build Fails: Missing C compiler

**Solution**: Install build tools as described in "Installing Native Build Tools" section.

### Reflection Errors at Runtime

**Solution**: Add missing classes to `NativeHintsConfig.java`. Spring Boot 3.x automatically generates most hints, but custom classes may need explicit registration.

### Out of Memory During Build

**Solution**: Increase Maven memory:
```bash
export MAVEN_OPTS="-Xmx8g"
mvn clean package -Pnative
```

### Application Fails to Connect to Database/RabbitMQ

**Solution**: Ensure services are running and configuration in `application.yml` matches your setup:
```bash
docker-compose up -d postgres rabbitmq
```

## Monitoring Native vs JVM Performance

To compare performance:

```bash
# Run both versions simultaneously
# Terminal 1 - JVM version
cd ../java21springboot
java -jar target/javaSpringBootReferenceService21.jar

# Terminal 2 - Native version
cd ../java21springbootgraal
./target/java21SpringBootGraalReferenceService

# Monitor with htop or Task Manager to see memory usage
# Use time or Measure-Command to measure startup time
```

## Additional Resources

- [GraalVM Documentation](https://www.graalvm.org/latest/docs/)
- [GraalVM Native Image](https://www.graalvm.org/latest/reference-manual/native-image/)
- [Spring Boot GraalVM Native Image Support](https://docs.spring.io/spring-boot/docs/current/reference/html/native-image.html)
- [Native Build Tools](https://graalvm.github.io/native-build-tools/)

## Summary

This GraalVM native image version demonstrates modern cloud-native Java development with:
- ✅ Fast startup times ideal for serverless and containerized deployments
- ✅ Lower memory consumption for better resource efficiency
- ✅ Same functionality as traditional JVM applications
- ✅ Production-ready Spring Boot 3.x with native image support

The trade-off is longer build times, but the runtime benefits often outweigh this cost in modern deployment scenarios.
