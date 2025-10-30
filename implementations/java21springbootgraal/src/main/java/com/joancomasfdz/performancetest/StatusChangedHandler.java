package com.joancomasfdz.performancetest;

import com.rabbitmq.client.Channel;
import com.joancomasfdz.java21.events.CloudEvent;
import com.joancomasfdz.java21.events.InstrumentStatusChangedEvent;
import com.joancomasfdz.java21.events.InstrumentStatusKpiUpdatedEvent;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.amqp.support.AmqpHeaders;
import org.springframework.aot.hint.annotation.RegisterReflectionForBinding;
import org.springframework.messaging.handler.annotation.Header;
import org.springframework.stereotype.Component;

import java.time.Instant;

/**
 * RabbitMQ message handler for instrument status change events.
 * This component listens for status change events on the configured queue,
 * processes them by persisting to the database, and publishes KPI update events.
 *
 * <p>The handler uses Spring's automatic message conversion via Jackson to
 * deserialize incoming messages into typed event objects, eliminating the need
 * for manual ObjectMapper usage.</p>
 *
 * @author Performance Test 2025
 * @version 1.0
 */
@Component
@RegisterReflectionForBinding({
    CloudEvent.class,
    InstrumentStatusChangedEvent.class,
    InstrumentStatusKpiUpdatedEvent.class
})
public class StatusChangedHandler {

    private static final Logger log = LoggerFactory.getLogger(StatusChangedHandler.class);

    /**
     * Unique identifier for this service in the event system.
     */
    private static final String SERVICE_URN = "urn:uuid:java21-springboot-graal";

    private final InstrumentStatusRepository repository;
    private final RabbitTemplate rabbitTemplate;

    /**
     * Constructs a new StatusChangedHandler with required dependencies.
     *
     * @param repository the repository for persisting instrument status records
     * @param rabbitTemplate the RabbitMQ template for publishing messages
     */
    public StatusChangedHandler(InstrumentStatusRepository repository,
                                RabbitTemplate rabbitTemplate) {
        this.repository = repository;
        this.rabbitTemplate = rabbitTemplate;
    }

    /**
     * Handles incoming instrument status change events from RabbitMQ.
     * This method is invoked automatically when a message arrives on the configured queue.
     * Spring automatically deserializes the message to the typed event object.
     *
     * <p>The method uses manual acknowledgment mode to ensure reliable message processing.
     * Messages are acknowledged only after successful processing and persistence. If an error
     * occurs, the message is negatively acknowledged and requeued for retry.</p>
     *
     * @param event the instrument status changed event
     * @param deliveryTag the delivery tag for manual acknowledgment
     * @param channel the RabbitMQ channel for acknowledgment operations
     */
    @RabbitListener(queues = RabbitConfig.QUEUE_NAME)
    public void handleStatusChanged(InstrumentStatusChangedEvent event,
                                    @Header(AmqpHeaders.DELIVERY_TAG) long deliveryTag,
                                    Channel channel) {
        try {
            log.info("Received event: {} from {}", event.getType(), event.getSource());

            // Extract data from typed event
            String deviceId = event.getDeviceId();
            String previousStatus = event.getPreviousStatus();
            String currentStatus = event.getCurrentStatus();

            log.info("Processing status change for device: {} from {} to {}",
                    deviceId, previousStatus, currentStatus);

            // Save to database
            InstrumentStatus status = new InstrumentStatus(
                    deviceId,
                    previousStatus,
                    currentStatus,
                    Instant.now()
            );
            repository.save(status);
            log.info("Saved instrument status to database with ID: {}", status.getId());

            // Publish KPI event
            publishKpiEvent(deviceId, currentStatus);

            // Manually acknowledge the message after successful processing
            channel.basicAck(deliveryTag, false);
            log.debug("Message acknowledged with delivery tag: {}", deliveryTag);

        } catch (Exception e) {
            log.error("Error processing status changed event", e);
            try {
                // Negatively acknowledge and requeue the message on error
                channel.basicNack(deliveryTag, false, true);
                log.warn("Message negatively acknowledged and requeued with delivery tag: {}", deliveryTag);
            } catch (Exception nackException) {
                log.error("Error sending negative acknowledgment", nackException);
            }
        }
    }

    /**
     * Publishes a KPI updated event to RabbitMQ.
     * This method creates and publishes an event to notify other services
     * that the KPI for an instrument has been updated.
     *
     * @param deviceId the unique identifier of the device
     * @param currentStatus the current status of the device
     */
    private void publishKpiEvent(String deviceId, String currentStatus) {
        try {
            // Create typed KPI event
            InstrumentStatusKpiUpdatedEvent kpiEvent = new InstrumentStatusKpiUpdatedEvent(
                    SERVICE_URN,
                    deviceId,
                    currentStatus
            );

            // Publish to exchange
            rabbitTemplate.convertAndSend(
                    RabbitConfig.EXCHANGE_NAME,
                    RabbitConfig.ROUTING_KEY_KPI_UPDATED,
                    kpiEvent
            );

            log.info("Published KPI event for device: {}", deviceId);

        } catch (Exception e) {
            log.error("Error publishing KPI event", e);
        }
    }
}
