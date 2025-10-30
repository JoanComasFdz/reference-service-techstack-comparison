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
 * RabbitMQ configuration for the Java 21 Spring Boot service.
 * This configuration class sets up the exchanges, queues, bindings, and message converters
 * required for handling instrument status events.
 */
@Configuration
public class RabbitConfig {

    public static final String EXCHANGE_NAME = "referenceservice.comparison";
    public static final String QUEUE_NAME = "javaSB";
    public static final String ROUTING_KEY_STATUS_CHANGED = "instrument.status.changed";
    public static final String ROUTING_KEY_KPI_UPDATED = "instrumentstatus.kpi.updated";

    @Value("${spring.rabbitmq.listener.simple.prefetch:50}")
    private int prefetchCount;

    /**
     * Creates the topic exchange for routing messages.
     *
     * @return a durable, non-auto-delete topic exchange
     */
    @Bean
    public TopicExchange exchange() {
        return new TopicExchange(EXCHANGE_NAME, true, false);
    }

    /**
     * Creates the queue for receiving instrument status change messages.
     *
     * @return a durable queue
     */
    @Bean
    public Queue queue() {
        return new Queue(QUEUE_NAME, true);
    }

    /**
     * Binds the queue to the exchange with the status changed routing key.
     *
     * @param queue the queue to bind
     * @param exchange the exchange to bind to
     * @return the binding configuration
     */
    @Bean
    public Binding binding(Queue queue, TopicExchange exchange) {
        return BindingBuilder.bind(queue).to(exchange).with(ROUTING_KEY_STATUS_CHANGED);
    }

    /**
     * Configures the message converter for JSON serialization/deserialization.
     * This converter automatically handles the transformation between Java objects and JSON messages.
     *
     * @return a Jackson-based JSON message converter
     */
    @Bean
    public MessageConverter messageConverter() {
        return new Jackson2JsonMessageConverter();
    }

    /**
     * Configures the RabbitMQ listener container factory with manual acknowledgment mode
     * and prefetch count for proper message handling and flow control.
     * The prefetch count is configurable via application.yml (spring.rabbitmq.listener.simple.prefetch)
     * with a default fallback value of 50.
     *
     * @param connectionFactory the RabbitMQ connection factory
     * @return a configured SimpleRabbitListenerContainerFactory
     */
    @Bean
    public SimpleRabbitListenerContainerFactory rabbitListenerContainerFactory(ConnectionFactory connectionFactory) {
        SimpleRabbitListenerContainerFactory factory = new SimpleRabbitListenerContainerFactory();
        factory.setConnectionFactory(connectionFactory);
        factory.setMessageConverter(messageConverter());
        factory.setPrefetchCount(prefetchCount);
        factory.setAcknowledgeMode(AcknowledgeMode.MANUAL);
        return factory;
    }
}
