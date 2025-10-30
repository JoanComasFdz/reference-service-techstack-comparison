package main

import (
	"log"
	"os"
	"os/signal"
	"syscall"
)

func main() {
	log.Println("Starting Go Reference Service for Performance Test 2025")

	// Load configuration
	config := LoadConfig()

	// Initialize database
	db, err := NewDatabase(config)
	if err != nil {
		log.Fatalf("Failed to initialize database: %v", err)
	}
	defer func() {
		if err := db.Close(); err != nil {
			log.Printf("Error closing database: %v", err)
		}
	}()

	// Initialize RabbitMQ service
	rabbitMQ, err := NewRabbitMQService(config, db)
	if err != nil {
		log.Fatalf("Failed to initialize RabbitMQ: %v", err)
	}
	defer func() {
		if err := rabbitMQ.Close(); err != nil {
			log.Printf("Error closing RabbitMQ: %v", err)
		}
	}()

	// Start consuming messages
	if err := rabbitMQ.StartConsuming(); err != nil {
		log.Fatalf("Failed to start consuming messages: %v", err)
	}

	// Initialize HTTP server
	server := NewServer(config, db)

	// Handle graceful shutdown
	go func() {
		sigChan := make(chan os.Signal, 1)
		signal.Notify(sigChan, os.Interrupt, syscall.SIGTERM)
		<-sigChan
		log.Println("Received shutdown signal, cleaning up...")
		if err := rabbitMQ.Close(); err != nil {
			log.Printf("Error closing RabbitMQ during shutdown: %v", err)
		}
		if err := db.Close(); err != nil {
			log.Printf("Error closing database during shutdown: %v", err)
		}
		os.Exit(0)
	}()

	// Start HTTP server (blocking)
	log.Println("Service is ready")
	if err := server.Start(); err != nil {
		log.Fatalf("Failed to start HTTP server: %v", err)
	}
}
