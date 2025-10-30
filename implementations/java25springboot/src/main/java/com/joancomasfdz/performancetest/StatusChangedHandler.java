package com.joancomasfdz.performancetest;

import com.rabbitmq.client.Channel;
import com.joancomasfdz.java25.events.InstrumentStatusChangedEvent;
import com.joancomasfdz.java25.events.InstrumentStatusKpiUpdatedEvent;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.amqp.support.AmqpHeaders;
import org.springframework.messaging.handler.annotation.Header;
import org.springframework.stereotype.Component;

import java.time.Instant;

/**
 * Message handler for instrument status change events received via RabbitMQ.
 * This component listens for status change events, persists them to the database,
 * and publishes corresponding KPI update events.
 */
@Component
public class StatusChangedHandler {

    private static final Logger log = LoggerFactory.getLogger(StatusChangedHandler.class);

    /**
     * The URN identifier for this Java 25 Spring Boot service instance.
     */
    private static final String SERVICE_URN = "urn:uuid:java25-springboot";

    private final InstrumentStatusRepository repository;
    private final RabbitTemplate rabbitTemplate;

    /**
     * Constructs a new StatusChangedHandler.
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
     * Spring AMQP automatically deserializes the message payload into the event object.
     * Messages are manually acknowledged after successful processing or negatively acknowledged
     * with requeue on error.
     *
     * @param event the deserialized instrument status changed event
     * @param deliveryTag the RabbitMQ delivery tag for message acknowledgment
     * @param channel the RabbitMQ channel for acknowledging messages
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
            log.info("Message acknowledged for device: {}", deviceId);

        } catch (Exception e) {
            log.error("Error processing status changed event", e);
            try {
                // Negatively acknowledge the message and requeue it for retry
                channel.basicNack(deliveryTag, false, true);
                log.info("Message negatively acknowledged and requeued");
            } catch (Exception nackException) {
                log.error("Failed to negatively acknowledge message", nackException);
            }
        }
    }

    /**
     * Publishes a KPI update event to RabbitMQ based on the current instrument status.
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
