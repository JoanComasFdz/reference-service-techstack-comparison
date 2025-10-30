package com.joancomasfdz.performancetest;

import org.springframework.amqp.core.AcknowledgeMode;
import org.springframework.amqp.core.Binding;
import org.springframework.amqp.core.BindingBuilder;
import org.springframework.amqp.core.Queue;
import org.springframework.amqp.core.TopicExchange;
import org.springframework.amqp.rabbit.config.SimpleRabbitListenerContainerFactory;
import org.springframework.amqp.rabbit.connection.ConnectionFactory;
import org.springframework.amqp.support.converter.Jackson2JsonMessageConverter;
import org.springframework.amqp.support.converter.MessageConverter;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * RabbitMQ configuration for the Java Spring Boot GraalVM service.
 * This configuration sets up the necessary exchanges, queues, bindings, and message
 * converters for RabbitMQ messaging integration.
 *
 * <p>The configuration creates a topic exchange for flexible routing patterns,
 * a durable queue for reliable message delivery, and bindings to route messages
 * based on routing keys.</p>
 *
 * @author Performance Test 2025
 * @version 1.0
 */
@Configuration
public class RabbitConfig {

    /**
     * Name of the RabbitMQ topic exchange used for all messaging.
     */
    public static final String EXCHANGE_NAME = "referenceservice.comparison";

    /**
     * Name of the queue that receives instrument status change events.
     */
    public static final String QUEUE_NAME = "javaSB25Graal";

    /**
     * Routing key for instrument status changed events.
     */
    public static final String ROUTING_KEY_STATUS_CHANGED = "instrument.status.changed";

    /**
     * Routing key for instrument status KPI updated events.
     */
    public static final String ROUTING_KEY_KPI_UPDATED = "instrumentstatus.kpi.updated";

    /**
     * Prefetch count for RabbitMQ listener, configurable via application.yml.
     * Defaults to 50 if not specified in configuration.
     */
    @Value("${spring.rabbitmq.listener.simple.prefetch:50}")
    private int prefetchCount;

    /**
     * Creates a topic exchange for message routing.
     * The exchange is durable (survives broker restarts) and non-auto-delete.
     *
     * @return configured topic exchange
     */
    @Bean
    public TopicExchange exchange() {
        return new TopicExchange(EXCHANGE_NAME, true, false);
    }

    /**
     * Creates a durable queue for receiving status change events.
     * Messages in this queue will survive broker restarts.
     *
     * @return configured queue
     */
    @Bean
    public Queue queue() {
        return new Queue(QUEUE_NAME, true);
    }

    /**
     * Creates a binding between the queue and exchange using the status changed routing key.
     * This ensures messages with the specified routing key are routed to the queue.
     *
     * @param queue the queue to bind
     * @param exchange the exchange to bind to
     * @return configured binding
     */
    @Bean
    public Binding binding(Queue queue, TopicExchange exchange) {
        return BindingBuilder.bind(queue).to(exchange).with(ROUTING_KEY_STATUS_CHANGED);
    }

    /**
     * Configures Jackson-based JSON message converter for automatic serialization
     * and deserialization of messages.
     *
     * @return Jackson JSON message converter
     */
    @Bean
    public MessageConverter messageConverter() {
        return new Jackson2JsonMessageConverter();
    }

    /**
     * Configures the RabbitMQ listener container factory with manual acknowledgment mode
     * and prefetch count for improved message processing control.
     *
     * <p>Manual acknowledgment mode allows the handler to explicitly acknowledge messages
     * after successful processing, ensuring that messages are not lost in case of processing
     * failures. The prefetch count limits the number of unacknowledged messages that can be
     * outstanding on the channel, providing better flow control.</p>
     *
     * <p>The prefetch count is configurable via application.yml
     * (spring.rabbitmq.listener.simple.prefetch) with a default fallback value of 50.</p>
     *
     * @param connectionFactory the RabbitMQ connection factory
     * @return configured listener container factory
     */
    @Bean
    public SimpleRabbitListenerContainerFactory rabbitListenerContainerFactory(
            ConnectionFactory connectionFactory) {
        SimpleRabbitListenerContainerFactory factory = new SimpleRabbitListenerContainerFactory();
        factory.setConnectionFactory(connectionFactory);
        factory.setMessageConverter(messageConverter());
        factory.setPrefetchCount(prefetchCount);
        factory.setAcknowledgeMode(AcknowledgeMode.MANUAL);
        return factory;
    }
}
