package com.joancomasfdz.performancetest;

import org.springframework.context.annotation.Configuration;

/**
 * GraalVM Native Image configuration.
 *
 * <p><strong>Note on Spring Boot 3.4+ Native Image Support:</strong></p>
 * <p>Spring Boot 3.4+ provides automatic AOT (Ahead-of-Time) processing and runtime hints
 * generation for most common use cases including:</p>
 * <ul>
 *   <li>JPA entities and repositories (Hibernate/Spring Data JPA)</li>
 *   <li>Spring beans and configuration classes</li>
 *   <li>Common framework components</li>
 * </ul>
 *
 * <p>For custom classes requiring reflection (such as event DTOs for JSON serialization),
 * use the {@code @RegisterReflectionForBinding} annotation directly on the component
 * that uses them. See {@link StatusChangedHandler} for an example.</p>
 *
 * <p>This configuration class is retained for potential future custom hints if needed,
 * but manual RuntimeHintsRegistrar implementations are typically unnecessary with
 * Spring Boot 3.4+.</p>
 *
 * @author Performance Test 2025
 * @version 1.0
 * @see StatusChangedHandler
 */
@Configuration
public class NativeHintsConfig {
    // Spring Boot 3.4+ handles most reflection hints automatically via AOT processing.
    // Use @RegisterReflectionForBinding on components for custom classes requiring reflection.
}
