package com.joancomasfdz.performancetest;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.joancomasfdz.java25.events.InstrumentStatusChangedEvent;
import com.joancomasfdz.java25.events.InstrumentStatusKpiUpdatedEvent;
import io.smallrye.reactive.messaging.annotations.Blocking;
import io.vertx.core.json.JsonObject;
import jakarta.enterprise.context.ApplicationScoped;
import jakarta.inject.Inject;
import jakarta.transaction.Transactional;
import org.eclipse.microprofile.reactive.messaging.Incoming;
import org.eclipse.microprofile.reactive.messaging.Outgoing;
import org.jboss.logging.Logger;

import java.time.Instant;

/**
 * Message handler for processing instrument status change events.
 *
 * This handler consumes status-changed events from RabbitMQ, persists the status
 * information to the database, and publishes KPI update events for downstream consumers.
 * The handler operates in a blocking manner with implicit auto-commit per database operation.
 */
@ApplicationScoped
public class StatusChangedHandler {

    private static final Logger LOG = Logger.getLogger(StatusChangedHandler.class);
    private static final String SERVICE_URN = "urn:uuid:java25-quarkus-graal";

    @Inject
    InstrumentStatusRepository repository;

    @Inject
    ObjectMapper objectMapper;

    /**
     * Processes incoming instrument status change events from RabbitMQ.
     *
     * This method serves as the main entry point for the event processing pipeline:
     * 1. Receives status-changed events from the RabbitMQ queue
     * 2. Parses and validates the event data
     * 3. Persists the status change to the database
     * 4. Creates and publishes a KPI update event
     *
     * The method executes in a blocking manner with implicit auto-commit per database operation
     * to ensure consistency with other services in the benchmark comparison.
     *
     * @param message the incoming JSON message from RabbitMQ containing the status change event
     * @return the KPI update event to be published to the kpi-updated channel
     * @throws RuntimeException if the event cannot be processed or persisted
     */
    @Incoming("status-changed")
    @Outgoing("kpi-updated")
    @Blocking
    @Transactional
    public InstrumentStatusKpiUpdatedEvent handleStatusChanged(JsonObject message) {
        try {
            InstrumentStatusChangedEvent event = parseEvent(message);
            logReceivedEvent(event);

            String deviceId = event.getDeviceId();
            String previousStatus = event.getPreviousStatus();
            String currentStatus = event.getCurrentStatus();

            logStatusChange(deviceId, previousStatus, currentStatus);

            InstrumentStatus status = persistInstrumentStatus(deviceId, previousStatus, currentStatus);

            return createAndPublishKpiEvent(deviceId, currentStatus);

        } catch (Exception e) {
            LOG.error("Error processing status changed event", e);
            throw new RuntimeException("Failed to process status changed event", e);
        }
    }

    /**
     * Parses the incoming JSON message into a typed event object.
     *
     * @param message the raw JSON message from RabbitMQ
     * @return the parsed InstrumentStatusChangedEvent
     * @throws Exception if JSON parsing fails
     */
    private InstrumentStatusChangedEvent parseEvent(JsonObject message) throws Exception {
        String jsonString = message.encode();
        return objectMapper.readValue(jsonString, InstrumentStatusChangedEvent.class);
    }

    /**
     * Logs information about the received event.
     *
     * @param event the parsed event containing type and source information
     */
    private void logReceivedEvent(InstrumentStatusChangedEvent event) {
        LOG.infof("Received event: %s from %s", event.getType(), event.getSource());
    }

    /**
     * Logs details about the status change being processed.
     *
     * @param deviceId the identifier of the device whose status changed
     * @param previousStatus the previous status of the device
     * @param currentStatus the new current status of the device
     */
    private void logStatusChange(String deviceId, String previousStatus, String currentStatus) {
        LOG.infof("Processing status change for device: %s from %s to %s",
                deviceId, previousStatus, currentStatus);
    }

    /**
     * Persists the instrument status change to the database.
     *
     * Creates a new InstrumentStatus entity with the provided status information
     * and the current timestamp, then persists it to the database.
     *
     * @param deviceId the identifier of the device
     * @param previousStatus the previous status of the device
     * @param currentStatus the new current status of the device
     * @return the persisted InstrumentStatus entity with generated ID
     */
    private InstrumentStatus persistInstrumentStatus(String deviceId, String previousStatus, String currentStatus) {
        InstrumentStatus status = new InstrumentStatus(
                deviceId,
                previousStatus,
                currentStatus,
                Instant.now()
        );
        repository.persist(status);
        LOG.infof("Saved instrument status to database with ID: %d", status.id);
        return status;
    }

    /**
     * Creates and prepares a KPI update event for publication.
     *
     * This event will be automatically published to the kpi-updated RabbitMQ exchange
     * for consumption by downstream services monitoring instrument KPIs.
     *
     * @param deviceId the identifier of the device
     * @param currentStatus the current status of the device
     * @return the KPI update event ready for publication
     */
    private InstrumentStatusKpiUpdatedEvent createAndPublishKpiEvent(String deviceId, String currentStatus) {
        InstrumentStatusKpiUpdatedEvent kpiEvent = new InstrumentStatusKpiUpdatedEvent(
                SERVICE_URN,
                deviceId,
                currentStatus
        );
        LOG.infof("Publishing KPI event for device: %s", deviceId);
        return kpiEvent;
    }
}
