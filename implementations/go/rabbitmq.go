package main

import (
	"encoding/json"
	"fmt"
	"log"

	amqp "github.com/rabbitmq/amqp091-go"
)

const (
	ExchangeName            = "referenceservice.comparison"
	QueueName               = "go"
	RoutingKeyStatusChanged = "instrument.status.changed"
	RoutingKeyKpiUpdated    = "instrumentstatus.kpi.updated"
	ServiceURN              = "urn:uuid:go-service"
)

// RabbitMQService handles RabbitMQ operations
type RabbitMQService struct {
	conn    *amqp.Connection
	channel *amqp.Channel
	config  *Config
	db      *Database
}

// NewRabbitMQService creates a new RabbitMQ service
func NewRabbitMQService(config *Config, db *Database) (*RabbitMQService, error) {
	connStr := fmt.Sprintf(
		"amqp://%s:%s@%s:%d/",
		config.RabbitMQUser,
		config.RabbitMQPassword,
		config.RabbitMQHost,
		config.RabbitMQPort,
	)

	conn, err := amqp.Dial(connStr)
	if err != nil {
		return nil, fmt.Errorf("failed to connect to RabbitMQ: %w", err)
	}

	channel, err := conn.Channel()
	if err != nil {
		if closeErr := conn.Close(); closeErr != nil {
			log.Printf("Error closing connection during cleanup: %v", closeErr)
		}
		return nil, fmt.Errorf("failed to open channel: %w", err)
	}

	service := &RabbitMQService{
		conn:    conn,
		channel: channel,
		config:  config,
		db:      db,
	}

	if err := service.setupExchangeAndQueue(); err != nil {
		if closeErr := service.Close(); closeErr != nil {
			log.Printf("Error closing service during cleanup: %v", closeErr)
		}
		return nil, fmt.Errorf("failed to setup exchange and queue: %w", err)
	}

	log.Println("RabbitMQ connection established")
	return service, nil
}

// setupExchangeAndQueue declares the exchange, queue, and bindings
func (r *RabbitMQService) setupExchangeAndQueue() error {
	// Declare exchange
	err := r.channel.ExchangeDeclare(
		ExchangeName, // name
		"topic",      // type
		true,         // durable
		false,        // auto-deleted
		false,        // internal
		false,        // no-wait
		nil,          // arguments
	)
	if err != nil {
		return fmt.Errorf("failed to declare exchange: %w", err)
	}

	// Declare queue
	_, err = r.channel.QueueDeclare(
		QueueName, // name
		true,      // durable
		false,     // delete when unused
		false,     // exclusive
		false,     // no-wait
		nil,       // arguments
	)
	if err != nil {
		return fmt.Errorf("failed to declare queue: %w", err)
	}

	// Bind queue to exchange
	err = r.channel.QueueBind(
		QueueName,               // queue name
		RoutingKeyStatusChanged, // routing key
		ExchangeName,            // exchange
		false,
		nil,
	)
	if err != nil {
		return fmt.Errorf("failed to bind queue: %w", err)
	}

	// Set QoS (prefetch count)
	err = r.channel.Qos(
		r.config.RabbitMQPrefetchCount, // prefetch count
		0,                              // prefetch size
		false,                          // global
	)
	if err != nil {
		return fmt.Errorf("failed to set QoS: %w", err)
	}

	log.Printf("RabbitMQ initialized: Exchange=%s, Queue=%s, Prefetch=%d", ExchangeName, QueueName, r.config.RabbitMQPrefetchCount)
	return nil
}

// StartConsuming starts consuming messages from the queue
func (r *RabbitMQService) StartConsuming() error {
	msgs, err := r.channel.Consume(
		QueueName, // queue
		"",        // consumer
		false,     // auto-ack
		false,     // exclusive
		false,     // no-local
		false,     // no-wait
		nil,       // args
	)
	if err != nil {
		return fmt.Errorf("failed to register consumer: %w", err)
	}

	log.Printf("Started consuming messages from queue: %s", QueueName)

	// Process messages
	go func() {
		for msg := range msgs {
			if err := r.processMessage(msg); err != nil {
				log.Printf("Error processing message: %v", err)
				// Send nack with requeue on failure
				if nackErr := msg.Nack(false, true); nackErr != nil {
					log.Printf("Failed to nack message: %v", nackErr)
				}
			} else {
				// Send ack on success
				if ackErr := msg.Ack(false); ackErr != nil {
					log.Printf("Failed to ack message: %v", ackErr)
				}
			}
		}
	}()

	return nil
}

// processMessage processes an incoming message
func (r *RabbitMQService) processMessage(msg amqp.Delivery) error {
	var event InstrumentStatusChangedEvent
	if err := json.Unmarshal(msg.Body, &event); err != nil {
		return fmt.Errorf("failed to unmarshal message: %w", err)
	}

	log.Printf("Received event: %s from %s", event.Type, event.Source)

	// Extract data from event
	deviceID := event.GetDeviceID()
	previousStatus := event.GetPreviousStatus()
	currentStatus := event.GetCurrentStatus()

	log.Printf("Processing status change for device: %s from %s to %s",
		deviceID, previousStatus, currentStatus)

	// Save to database
	_, err := r.db.SaveInstrumentStatus(deviceID, previousStatus, currentStatus)
	if err != nil {
		return fmt.Errorf("failed to save to database: %w", err)
	}

	// Publish KPI event
	if err := r.publishKpiEvent(deviceID, currentStatus); err != nil {
		return fmt.Errorf("failed to publish KPI event: %w", err)
	}

	return nil
}

// publishKpiEvent publishes a KPI updated event
func (r *RabbitMQService) publishKpiEvent(deviceID, currentStatus string) error {
	kpiEvent := NewInstrumentStatusKpiUpdatedEvent(
		ServiceURN,
		deviceID,
		currentStatus,
	)

	body, err := json.Marshal(kpiEvent)
	if err != nil {
		return fmt.Errorf("failed to marshal KPI event: %w", err)
	}

	err = r.channel.Publish(
		ExchangeName,         // exchange
		RoutingKeyKpiUpdated, // routing key
		false,                // mandatory
		false,                // immediate
		amqp.Publishing{
			ContentType: "application/json",
			Body:        body,
		},
	)
	if err != nil {
		return fmt.Errorf("failed to publish message: %w", err)
	}

	log.Printf("Published KPI event for device: %s", deviceID)
	return nil
}

// Close closes the RabbitMQ connection and channel
func (r *RabbitMQService) Close() error {
	var errs []error
	if r.channel != nil {
		if err := r.channel.Close(); err != nil {
			errs = append(errs, fmt.Errorf("channel close: %w", err))
		}
	}
	if r.conn != nil {
		if err := r.conn.Close(); err != nil {
			errs = append(errs, fmt.Errorf("connection close: %w", err))
		}
	}
	if len(errs) > 0 {
		return fmt.Errorf("errors closing RabbitMQ: %v", errs)
	}
	return nil
}
