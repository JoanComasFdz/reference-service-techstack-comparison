/**
 * CloudEvents Model Library for Event-Driven Systems.
 *
 * <h2>Overview</h2>
 * <p>This package provides a reusable, framework-agnostic Java 21 event model based on the
 * <a href="https://cloudevents.io/">CloudEvents specification v1.0</a>. The library enables
 * consistent event modeling across event-driven architectures, supporting both
 * standard CloudEvents attributes and optional extensions.</p>
 *
 * <h2>Key Features</h2>
 * <ul>
 *   <li>Framework-agnostic POJOs compatible with Spring Boot, Quarkus, and plain Java</li>
 *   <li>CloudEvents v1.0 compliant with optional extensions</li>
 *   <li>Built-in Jackson JSON serialization support via annotations</li>
 *   <li>Type-safe event data payloads with immutable collections</li>
 *   <li>Comprehensive JavaDoc and code examples</li>
 *   <li>Proper equals/hashCode/toString implementations</li>
 * </ul>
 *
 * <h2>Core Classes</h2>
 * <dl>
 *   <dt>{@link com.joancomasfdz.java21.events.CloudEvent}</dt>
 *   <dd>Base class for all CloudEvents, implementing CloudEvents v1.0 specification with
 *       optional extensions. Provides common event attributes and helper methods.</dd>
 *
 *   <dt>{@link com.joancomasfdz.java21.events.InstrumentStatusChangedEvent}</dt>
 *   <dd>Event representing a change in instrument operational status. Contains device
 *       identification and status transition information (previous and current status).</dd>
 *
 *   <dt>{@link com.joancomasfdz.java21.events.InstrumentStatusKpiUpdatedEvent}</dt>
 *   <dd>Event representing a KPI update for instrument status. Contains device identification
 *       and current operational status metrics.</dd>
 * </dl>
 *
 * <h2>Usage Example</h2>
 * <pre>
 * // Create an instrument status change event
 * InstrumentStatusChangedEvent event = new InstrumentStatusChangedEvent(
 *     "urn:uuid:device-simulator",
 *     "device-123",
 *     "idle",
 *     "running"
 * );
 *
 * // Access event properties
 * String eventId = event.getId();
 * String deviceId = event.getDeviceId();
 * String status = event.getCurrentStatus();
 *
 * // Serialize to JSON (using Jackson or similar)
 * ObjectMapper mapper = new ObjectMapper();
 * String json = mapper.writeValueAsString(event);
 *
 * // Deserialize from JSON
 * InstrumentStatusChangedEvent deserialized =
 *     mapper.readValue(json, InstrumentStatusChangedEvent.class);
 * </pre>
 *
 * <h2>Event Schema Compliance</h2>
 * <p>All events in this package can reference JSON schemas for validation. The schema URI
 * is included in each event's {@code dataschema} field as per CloudEvents specification.</p>
 *
 * <h2>CloudEvents Attributes</h2>
 * <p>All events support the following standard CloudEvents attributes:</p>
 * <ul>
 *   <li><strong>id</strong> - Unique event identifier (UUID)</li>
 *   <li><strong>specversion</strong> - CloudEvents specification version (1.0)</li>
 *   <li><strong>source</strong> - Event source URI (e.g., urn:uuid:service-name)</li>
 *   <li><strong>type</strong> - Event type identifier (e.g., instrument.status.changed)</li>
 *   <li><strong>time</strong> - Event timestamp in ISO 8601 format</li>
 *   <li><strong>datacontenttype</strong> - Content type of data payload (application/json)</li>
 *   <li><strong>dataschema</strong> - URI of the JSON schema for the data payload</li>
 *   <li><strong>data</strong> - Event-specific data payload</li>
 * </ul>
 *
 * <h2>CloudEvents Extensions</h2>
 * <ul>
 *   <li><strong>privacyrelevant</strong> - Flag indicating privacy-relevant data</li>
 *   <li><strong>kind</strong> - Event kind (e.g., "event", "command")</li>
 * </ul>
 *
 * <h2>Best Practices</h2>
 * <ul>
 *   <li>Use specific event types (e.g., InstrumentStatusChangedEvent) rather than generic
 *       CloudEvent for type safety</li>
 *   <li>Always specify a meaningful source URI that identifies the event producer</li>
 *   <li>Use the provided field constants (e.g., FIELD_DEVICE_ID) when working with event data</li>
 *   <li>Leverage the type-safe getter methods for accessing event data fields</li>
 *   <li>Consider implementing custom event types by extending CloudEvent for domain-specific needs</li>
 * </ul>
 *
 * <h2>Thread Safety</h2>
 * <p>Event instances are mutable POJOs designed for single-threaded use. If sharing events
 * across threads, consider using immutable copies or proper synchronization. The data payload
 * uses immutable collections (Map.of()) for thread-safe read access.</p>
 *
 * <h2>JSON Serialization</h2>
 * <p>All classes use Jackson {@code @JsonProperty} annotations to ensure correct JSON
 * field mapping. The library requires {@code jackson-annotations} as an optional dependency.
 * Field names in JSON maintain the CloudEvents specification naming (e.g., "datacontenttype")
 * while Java fields follow camelCase conventions (e.g., dataContentType).</p>
 *
 * @since 1.0.0
 * @version 1.0.0
 * @see <a href="https://cloudevents.io/">CloudEvents Specification</a>
 */
package com.joancomasfdz.java21.events;
