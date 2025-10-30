package com.joancomasfdz.performancetest;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

/**
 * Main application class for the Java 21 Spring Boot Reference Service.
 * This service demonstrates a reference implementation for processing instrument status
 * events via RabbitMQ, persisting them to PostgreSQL, and exposing KPI endpoints.
 */
@SpringBootApplication
public class Java21SpringBootReferenceServiceApplication {

    /**
     * Application entry point.
     *
     * @param args command-line arguments
     */
    public static void main(String[] args) {
        SpringApplication.run(Java21SpringBootReferenceServiceApplication.class, args);
    }
}
