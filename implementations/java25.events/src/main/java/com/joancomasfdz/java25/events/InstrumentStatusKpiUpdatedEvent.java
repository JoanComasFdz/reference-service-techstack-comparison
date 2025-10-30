package com.joancomasfdz.java25.events;

import java.time.Instant;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;

/**
 * Event representing a KPI (Key Performance Indicator) update for instrument status.
 *
 * <p>This event is published when the operational KPIs of a medical instrument need to be
 * updated or reported. Unlike {@link InstrumentStatusChangedEvent} which tracks status
 * transitions, this event focuses on the current state and metrics of the instrument.</p>
 *
 * <p>The event type is {@code "instrumentstatus.kpi.updated"} and follows the CloudEvents
 * v1.0 specification.</p>
 *
 * <p>Example usage:</p>
 * <pre>
 * InstrumentStatusKpiUpdatedEvent event = new InstrumentStatusKpiUpdatedEvent(
 *     "urn:uuid:java21-springboot",
 *     "device-123",
 *     "running"
 * );
 * </pre>
 *
 * @see CloudEvent
 * @see InstrumentStatusChangedEvent
 */
public class InstrumentStatusKpiUpdatedEvent extends CloudEvent {

    /**
     * Field key for device ID in the event data payload.
     */
    public static final String FIELD_DEVICE_ID = "deviceId";

    /**
     * Field key for current status in the event data payload.
     */
    public static final String FIELD_CURRENT_STATUS = "currentStatus";

    /**
     * Event type identifier for instrument status KPI updated events.
     */
    public static final String EVENT_TYPE = "instrumentstatus.kpi.updated";

    /**
     * Schema URI for instrument status KPI updated events.
     */
    public static final String SCHEMA_URI = "https://example.com/schemas/events-catalog/instrumentstatus.kpi.updated.schema.json";

    /**
     * Creates a new InstrumentStatusKpiUpdatedEvent with the specified data.
     *
     * <p>This constructor automatically generates a unique event ID and sets the current
     * timestamp. All CloudEvents fields are initialized with appropriate default values.</p>
     *
     * @param source The source URI of the event (e.g., "urn:uuid:java21-springboot")
     * @param deviceId The unique identifier of the device
     * @param currentStatus The current operational status of the instrument
     */
    public InstrumentStatusKpiUpdatedEvent(String source, String deviceId, String currentStatus) {
        initializeEvent(
                source,
                EVENT_TYPE,
                SCHEMA_URI,
                UUID.randomUUID().toString(),
                Instant.now().toString()
        );

        setData(Map.of(
                FIELD_DEVICE_ID, deviceId,
                FIELD_CURRENT_STATUS, currentStatus
        ));
    }

    /**
     * Default constructor for deserialization.
     *
     * <p>This constructor is required for JSON deserialization frameworks like Jackson.
     * When deserializing, all fields will be populated via setter methods.</p>
     */
    public InstrumentStatusKpiUpdatedEvent() {
    }

    /**
     * Gets the device ID from the event data.
     *
     * @return The unique device identifier, or null if not present in the data payload
     */
    public String getDeviceId() {
        return getDataField(FIELD_DEVICE_ID);
    }

    /**
     * Gets the current status from the event data.
     *
     * @return The current operational status of the instrument, or null if not present
     */
    public String getCurrentStatus() {
        return getDataField(FIELD_CURRENT_STATUS);
    }

    /**
     * Checks equality based on all event fields including the parent class fields.
     *
     * @param o The object to compare with
     * @return True if the objects are equal, false otherwise
     */
    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        if (!super.equals(o)) return false;
        InstrumentStatusKpiUpdatedEvent that = (InstrumentStatusKpiUpdatedEvent) o;
        // All relevant fields are in parent class and data map
        return true;
    }

    /**
     * Generates hash code based on all event fields including parent class fields.
     *
     * @return The hash code value
     */
    @Override
    public int hashCode() {
        return Objects.hash(super.hashCode());
    }

    /**
     * Returns a string representation of the event including typed data fields.
     *
     * @return A string containing all event field values
     */
    @Override
    public String toString() {
        return "InstrumentStatusKpiUpdatedEvent{" +
                "deviceId='" + getDeviceId() + '\'' +
                ", currentStatus='" + getCurrentStatus() + '\'' +
                ", " + super.toString() +
                '}';
    }
}
