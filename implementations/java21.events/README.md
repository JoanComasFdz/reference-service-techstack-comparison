# CloudEvent Model

Reusable Java library for CloudEvents v1.0 specification.

## Overview

This library provides a framework-agnostic POJO model for CloudEvents. It can be used with any Java application including Spring Boot, Quarkus, or plain Java applications.

## Features

- ✅ Framework-independent (no Spring Boot dependencies)
- ✅ Java 21 compatible
- ✅ Minimal dependencies (only Jackson annotations, marked as optional)
- ✅ Implements CloudEvents v1.0 specification with extensions
- ✅ Simple POJO with getters/setters for easy JSON serialization

## Build

```bash
mvn clean package
```

This produces `java21-events-1.0.0.jar` in the `target/` directory.

**Important:** For proper Maven integration, use `mvn clean install` to install the library to your local Maven repository.

## Usage

### Maven Dependency (System Scope)

For local development, you can reference the built JAR:

```xml
<dependency>
    <groupId>com.joancomasfdz.java21.events</groupId>
    <artifactId>java21-events</artifactId>
    <version>1.0.0</version>
    <scope>system</scope>
    <systemPath>${project.basedir}/../java21.events/target/java21-events-1.0.0.jar</systemPath>
</dependency>
```

### Maven Dependency (Install to Local Repository)

For better Maven integration, install to your local repository:

```bash
cd java21.events
mvn clean install
```

Then reference it normally:

```xml
<dependency>
    <groupId>com.joancomasfdz.java21.events</groupId>
    <artifactId>java21-events</artifactId>
    <version>1.0.0</version>
</dependency>
```

### Code Example

#### Using the events

```java
import com.joancomasfdz.java21.events.CloudEvent;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import java.time.Instant;

// Create a new event
CloudEvent event = new CloudEvent();
event.setId(UUID.randomUUID().toString());
event.setSpecversion("1.0");
event.setSource("urn:uuid:my-service");
event.setType("instrument.status.changed");
event.setTime(Instant.now().toString());
event.setPrivacyrelevant(false);
event.setDatacontenttype("application/json");
event.setDataschema("https://example.com/schemas/events-catalog/instrument.status.changed.schema.json");
event.setKind("event");

// Add data payload
Map<String, Object> data = new HashMap<>();
data.put("deviceId", "DEVICE-001");
data.put("previousStatus", "IDLE");
data.put("currentStatus", "RUNNING");
event.setData(data);

// Serialize with any JSON library (Jackson, Gson, etc.)
```

```java
import com.joancomasfdz.java21.events.InstrumentStatusChangedEvent;
import com.joancomasfdz.java21.events.InstrumentStatusKpiUpdatedEvent;

// Create instrument status changed event
InstrumentStatusChangedEvent statusEvent = new InstrumentStatusChangedEvent(
    "urn:uuid:my-service",
    "DEVICE-001",
    "IDLE",
    "RUNNING"
);

// Access typed data
String deviceId = statusEvent.getDeviceId();
String previousStatus = statusEvent.getPreviousStatus();
String currentStatus = statusEvent.getCurrentStatus();

// Create KPI updated event
InstrumentStatusKpiUpdatedEvent kpiEvent = new InstrumentStatusKpiUpdatedEvent(
    "urn:uuid:my-service",
    "DEVICE-001",
    "RUNNING"
);

// Access typed data
String kpiDeviceId = kpiEvent.getDeviceId();
String kpiStatus = kpiEvent.getCurrentStatus();
```

## License

Library for demonstration purposes.
