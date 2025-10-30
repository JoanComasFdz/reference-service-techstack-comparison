package com.joancomasfdz.performancetest;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

/**
 * Main entry point for the Java 25 Spring Boot GraalVM Reference Service.
 * This application demonstrates a microservice built with Java 25, Spring Boot 3.x,
 * and compiled to a native executable using GraalVM Native Image.
 *
 * <p>The service handles instrument status change events from RabbitMQ, persists them
 * to a PostgreSQL database, and publishes KPI update events. It also exposes a REST
 * endpoint for querying the latest instrument status.</p>
 *
 * <p>Key features:</p>
 * <ul>
 *   <li>Native image compilation with GraalVM for faster startup and lower memory footprint</li>
 *   <li>RabbitMQ integration for event-driven architecture</li>
 *   <li>JPA/Hibernate for database persistence</li>
 *   <li>REST API for KPI exposure</li>
 * </ul>
 *
 * @author Performance Test 2025
 * @version 1.0
 */
@SpringBootApplication
public class Java25SpringBootGraalReferenceServiceApplication {

    /**
     * Main method that bootstraps the Spring Boot application.
     *
     * @param args command-line arguments passed to the application
     */
    public static void main(String[] args) {
        SpringApplication.run(Java25SpringBootGraalReferenceServiceApplication.class, args);
    }
}
