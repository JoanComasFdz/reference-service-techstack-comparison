package com.joancomasfdz.java21.events;

import java.time.Instant;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;

/**
 * Event representing a change in instrument status.
 *
 * <p>This event is triggered when a medical instrument transitions from one operational
 * status to another (e.g., from "idle" to "running", or from "running" to "error").
 * It extends {@link CloudEvent} with a typed data payload containing device identification
 * and status transition information.</p>
 *
 * <p>The event type is {@code "instrument.status.changed"} and follows the CloudEvents
 * v1.0 specification.</p>
 *
 * <p>Example usage:</p>
 * <pre>
 * InstrumentStatusChangedEvent event = new InstrumentStatusChangedEvent(
 *     "urn:uuid:device-simulator",
 *     "device-123",
 *     "idle",
 *     "running"
 * );
 * </pre>
 *
 * @see CloudEvent
 */
public class InstrumentStatusChangedEvent extends CloudEvent {

    /**
     * Field key for device ID in the event data payload.
     */
    public static final String FIELD_DEVICE_ID = "deviceId";

    /**
     * Field key for previous status in the event data payload.
     */
    public static final String FIELD_PREVIOUS_STATUS = "previousStatus";

    /**
     * Field key for current status in the event data payload.
     */
    public static final String FIELD_CURRENT_STATUS = "currentStatus";

    /**
     * Event type identifier for instrument status changed events.
     */
    public static final String EVENT_TYPE = "instrument.status.changed";

    /**
     * Schema URI for instrument status changed events.
     */
    public static final String SCHEMA_URI = "https://example.com/schemas/events-catalog/instrument.status.changed.schema.json";

    /**
     * Creates a new InstrumentStatusChangedEvent with the specified data.
     *
     * <p>This constructor automatically generates a unique event ID and sets the current
     * timestamp. All CloudEvents fields are initialized with appropriate default values.</p>
     *
     * @param source The source URI of the event (e.g., "urn:uuid:device-simulator")
     * @param deviceId The unique identifier of the device
     * @param previousStatus The previous operational status of the instrument
     * @param currentStatus The current operational status of the instrument
     */
    public InstrumentStatusChangedEvent(String source, String deviceId, String previousStatus, String currentStatus) {
        initializeEvent(
                source,
                EVENT_TYPE,
                SCHEMA_URI,
                UUID.randomUUID().toString(),
                Instant.now().toString()
        );

        setData(Map.of(
                FIELD_DEVICE_ID, deviceId,
                FIELD_PREVIOUS_STATUS, previousStatus,
                FIELD_CURRENT_STATUS, currentStatus
        ));
    }

    /**
     * Default constructor for deserialization.
     *
     * <p>This constructor is required for JSON deserialization frameworks like Jackson.
     * When deserializing, all fields will be populated via setter methods.</p>
     */
    public InstrumentStatusChangedEvent() {
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
     * Gets the previous status from the event data.
     *
     * @return The previous operational status of the instrument, or null if not present
     */
    public String getPreviousStatus() {
        return getDataField(FIELD_PREVIOUS_STATUS);
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
        InstrumentStatusChangedEvent that = (InstrumentStatusChangedEvent) o;
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
        return "InstrumentStatusChangedEvent{" +
                "deviceId='" + getDeviceId() + '\'' +
                ", previousStatus='" + getPreviousStatus() + '\'' +
                ", currentStatus='" + getCurrentStatus() + '\'' +
                ", " + super.toString() +
                '}';
    }
}
