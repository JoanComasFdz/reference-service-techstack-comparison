package com.joancomasfdz.java21.events;

import com.fasterxml.jackson.annotation.JsonProperty;

import java.util.Map;
import java.util.Objects;

/**
 * CloudEvent model following the CloudEvents v1.0 specification.
 *
 * <p>This is a framework-agnostic POJO that can be used with any Java application,
 * including Spring Boot, Quarkus, or plain Java. It follows the CloudEvents v1.0
 * specification with optional extensions for privacy and event classification.</p>
 *
 * <p>The event model supports the following standard CloudEvents attributes:</p>
 * <ul>
 *   <li>id - Unique event identifier</li>
 *   <li>specversion - CloudEvents specification version</li>
 *   <li>source - Event source URI</li>
 *   <li>type - Event type identifier</li>
 *   <li>time - Event timestamp in ISO 8601 format</li>
 *   <li>datacontenttype - Content type of the data payload</li>
 *   <li>dataschema - Schema URI for the data payload</li>
 *   <li>data - Event-specific data payload</li>
 * </ul>
 *
 * <p>Optional CloudEvents extensions:</p>
 * <ul>
 *   <li>privacyrelevant - Indicates if the event contains privacy-relevant data</li>
 *   <li>kind - Event kind (e.g., "event", "command")</li>
 * </ul>
 *
 * @see <a href="https://cloudevents.io/">CloudEvents Specification</a>
 */
public class CloudEvent {

    /**
     * Unique identifier for this event instance.
     */
    @JsonProperty("id")
    private String id;

    /**
     * Version of the CloudEvents specification being used (e.g., "1.0").
     */
    @JsonProperty("specversion")
    private String specVersion;

    /**
     * Identifies the context in which an event happened (e.g., "urn:uuid:device-simulator").
     */
    @JsonProperty("source")
    private String source;

    /**
     * Describes the type of event (e.g., "instrument.status.changed").
     */
    @JsonProperty("type")
    private String type;

    /**
     * Timestamp of when the event occurred (ISO 8601 format).
     */
    @JsonProperty("time")
    private String time;

    /**
     * Indicates whether this event contains privacy-relevant data.
     */
    @JsonProperty("privacyrelevant")
    private Boolean privacyRelevant;

    /**
     * Content type of the data value (e.g., "application/json").
     */
    @JsonProperty("datacontenttype")
    private String dataContentType;

    /**
     * URI identifying the schema that the data adheres to.
     */
    @JsonProperty("dataschema")
    private String dataSchema;

    /**
     * Kind of event (e.g., "event", "command").
     */
    @JsonProperty("kind")
    private String kind;

    /**
     * Event-specific data payload.
     */
    @JsonProperty("data")
    private Map<String, Object> data;

    /**
     * Default constructor for deserialization.
     */
    public CloudEvent() {
    }

    /**
     * Initializes common event fields with standard values.
     * This method should be called by subclass constructors to set up common event attributes.
     *
     * @param source The source URI of the event
     * @param type The event type identifier
     * @param dataSchema The URI of the data schema
     * @param id The unique event identifier
     * @param time The event timestamp in ISO 8601 format
     */
    protected void initializeEvent(String source, String type, String dataSchema, String id, String time) {
        this.id = id;
        this.specVersion = "1.0";
        this.source = source;
        this.type = type;
        this.time = time;
        this.privacyRelevant = false;
        this.dataContentType = "application/json";
        this.dataSchema = dataSchema;
        this.kind = "event";
    }

    /**
     * Helper method to retrieve a typed field from the data map.
     * Reduces duplication in getter methods of subclasses.
     *
     * @param <T> The expected type of the field value
     * @param fieldName The name of the field to retrieve
     * @return The field value cast to type T, or null if data is null or field doesn't exist
     */
    @SuppressWarnings("unchecked")
    protected <T> T getDataField(String fieldName) {
        return data != null ? (T) data.get(fieldName) : null;
    }

    // Getters and Setters

    /**
     * Gets the unique event identifier.
     *
     * @return The event ID
     */
    public String getId() {
        return id;
    }

    /**
     * Sets the unique event identifier.
     *
     * @param id The event ID to set
     */
    public void setId(String id) {
        this.id = id;
    }

    /**
     * Gets the CloudEvents specification version.
     *
     * @return The specification version (e.g., "1.0")
     */
    public String getSpecversion() {
        return specVersion;
    }

    /**
     * Sets the CloudEvents specification version.
     *
     * @param specVersion The specification version to set
     */
    public void setSpecversion(String specVersion) {
        this.specVersion = specVersion;
    }

    /**
     * Gets the event source URI.
     *
     * @return The source URI
     */
    public String getSource() {
        return source;
    }

    /**
     * Sets the event source URI.
     *
     * @param source The source URI to set
     */
    public void setSource(String source) {
        this.source = source;
    }

    /**
     * Gets the event type identifier.
     *
     * @return The event type
     */
    public String getType() {
        return type;
    }

    /**
     * Sets the event type identifier.
     *
     * @param type The event type to set
     */
    public void setType(String type) {
        this.type = type;
    }

    /**
     * Gets the event timestamp.
     *
     * @return The timestamp in ISO 8601 format
     */
    public String getTime() {
        return time;
    }

    /**
     * Sets the event timestamp.
     *
     * @param time The timestamp to set (ISO 8601 format)
     */
    public void setTime(String time) {
        this.time = time;
    }

    /**
     * Gets the privacy-relevant flag.
     *
     * @return True if the event contains privacy-relevant data, false otherwise
     */
    public Boolean getPrivacyrelevant() {
        return privacyRelevant;
    }

    /**
     * Sets the privacy-relevant flag.
     *
     * @param privacyRelevant True if the event contains privacy-relevant data
     */
    public void setPrivacyrelevant(Boolean privacyRelevant) {
        this.privacyRelevant = privacyRelevant;
    }

    /**
     * Gets the data content type.
     *
     * @return The content type (e.g., "application/json")
     */
    public String getDatacontenttype() {
        return dataContentType;
    }

    /**
     * Sets the data content type.
     *
     * @param dataContentType The content type to set
     */
    public void setDatacontenttype(String dataContentType) {
        this.dataContentType = dataContentType;
    }

    /**
     * Gets the data schema URI.
     *
     * @return The schema URI
     */
    public String getDataschema() {
        return dataSchema;
    }

    /**
     * Sets the data schema URI.
     *
     * @param dataSchema The schema URI to set
     */
    public void setDataschema(String dataSchema) {
        this.dataSchema = dataSchema;
    }

    /**
     * Gets the event kind.
     *
     * @return The event kind (e.g., "event", "command")
     */
    public String getKind() {
        return kind;
    }

    /**
     * Sets the event kind.
     *
     * @param kind The event kind to set
     */
    public void setKind(String kind) {
        this.kind = kind;
    }

    /**
     * Gets the event data payload.
     *
     * @return The data map containing event-specific data
     */
    public Map<String, Object> getData() {
        return data;
    }

    /**
     * Sets the event data payload.
     *
     * @param data The data map to set
     */
    public void setData(Map<String, Object> data) {
        this.data = data;
    }

    /**
     * Checks equality based on all event fields.
     *
     * @param o The object to compare with
     * @return True if the objects are equal, false otherwise
     */
    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        CloudEvent that = (CloudEvent) o;
        return Objects.equals(id, that.id) &&
                Objects.equals(specVersion, that.specVersion) &&
                Objects.equals(source, that.source) &&
                Objects.equals(type, that.type) &&
                Objects.equals(time, that.time) &&
                Objects.equals(privacyRelevant, that.privacyRelevant) &&
                Objects.equals(dataContentType, that.dataContentType) &&
                Objects.equals(dataSchema, that.dataSchema) &&
                Objects.equals(kind, that.kind) &&
                Objects.equals(data, that.data);
    }

    /**
     * Generates hash code based on all event fields.
     *
     * @return The hash code value
     */
    @Override
    public int hashCode() {
        return Objects.hash(id, specVersion, source, type, time,
                privacyRelevant, dataContentType, dataSchema, kind, data);
    }

    /**
     * Returns a string representation of the event.
     *
     * @return A string containing all event field values
     */
    @Override
    public String toString() {
        return "CloudEvent{" +
                "id='" + id + '\'' +
                ", specVersion='" + specVersion + '\'' +
                ", source='" + source + '\'' +
                ", type='" + type + '\'' +
                ", time='" + time + '\'' +
                ", privacyRelevant=" + privacyRelevant +
                ", dataContentType='" + dataContentType + '\'' +
                ", dataSchema='" + dataSchema + '\'' +
                ", kind='" + kind + '\'' +
                ", data=" + data +
                '}';
    }
}
