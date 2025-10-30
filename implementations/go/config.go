package main

import (
	"os"
	"strconv"
)

// Config holds the application configuration
type Config struct {
	ServerPort            int
	DBHost                string
	DBPort                int
	DBUser                string
	DBPassword            string
	DBName                string
	RabbitMQHost          string
	RabbitMQPort          int
	RabbitMQUser          string
	RabbitMQPassword      string
	RabbitMQPrefetchCount int
}

// LoadConfig loads configuration from environment variables with defaults
func LoadConfig() *Config {
	return &Config{
		ServerPort:            getEnvAsInt("SERVER_PORT", 8094),
		DBHost:                getEnv("DB_HOST", "localhost"),
		DBPort:                getEnvAsInt("DB_PORT", 5432),
		DBUser:                getEnv("DB_USER", "admin"),
		DBPassword:            getEnv("DB_PASSWORD", "admin"),
		DBName:                getEnv("DB_NAME", "go_db"),
		RabbitMQHost:          getEnv("RABBITMQ_HOST", "localhost"),
		RabbitMQPort:          getEnvAsInt("RABBITMQ_PORT", 5672),
		RabbitMQUser:          getEnv("RABBITMQ_USER", "admin"),
		RabbitMQPassword:      getEnv("RABBITMQ_PASSWORD", "admin"),
		RabbitMQPrefetchCount: getEnvAsInt("RABBITMQ_PREFETCH_COUNT", 50),
	}
}

// getEnv gets an environment variable or returns a default value
func getEnv(key, defaultValue string) string {
	value := os.Getenv(key)
	if value == "" {
		return defaultValue
	}
	return value
}

// getEnvAsInt gets an environment variable as an integer or returns a default value
func getEnvAsInt(key string, defaultValue int) int {
	valueStr := os.Getenv(key)
	if valueStr == "" {
		return defaultValue
	}
	value, err := strconv.Atoi(valueStr)
	if err != nil {
		return defaultValue
	}
	return value
}
