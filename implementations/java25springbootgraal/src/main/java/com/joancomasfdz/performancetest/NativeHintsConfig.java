package com.joancomasfdz.performancetest;

import com.joancomasfdz.java25.events.CloudEvent;
import org.springframework.aot.hint.MemberCategory;
import org.springframework.aot.hint.RuntimeHints;
import org.springframework.aot.hint.RuntimeHintsRegistrar;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.ImportRuntimeHints;

/**
 * GraalVM Native Image runtime hints configuration.
 * This configuration class registers classes that need reflection support at runtime
 * in the native image, enabling JSON serialization/deserialization and JPA operations
 * to work correctly in the compiled native binary.
 *
 * <p>GraalVM's ahead-of-time compilation cannot automatically detect all reflection usage,
 * so explicit registration is required for classes used dynamically at runtime.</p>
 *
 * @author Performance Test 2025
 * @version 1.0
 */
@Configuration
@ImportRuntimeHints(NativeHintsConfig.NativeHintsRegistrarImpl.class)
public class NativeHintsConfig {

    /**
     * Implementation of {@link RuntimeHintsRegistrar} that registers reflection hints
     * for classes used in JSON serialization and JPA operations.
     */
    static class NativeHintsRegistrarImpl implements RuntimeHintsRegistrar {

        /**
         * Registers runtime hints for reflection access to event and entity classes.
         * This method is called during the native image build process.
         *
         * @param hints the runtime hints to configure
         * @param classLoader the class loader to use for loading classes
         */
        @Override
        public void registerHints(RuntimeHints hints, ClassLoader classLoader) {
            // Register CloudEvent for reflection (JSON serialization/deserialization)
            hints.reflection().registerType(
                    CloudEvent.class,
                    MemberCategory.INVOKE_DECLARED_CONSTRUCTORS,
                    MemberCategory.INVOKE_PUBLIC_CONSTRUCTORS,
                    MemberCategory.INVOKE_DECLARED_METHODS,
                    MemberCategory.INVOKE_PUBLIC_METHODS,
                    MemberCategory.DECLARED_FIELDS,
                    MemberCategory.PUBLIC_FIELDS
            );

            // Register InstrumentStatus entity for reflection (JPA and JSON)
            hints.reflection().registerType(
                    InstrumentStatus.class,
                    MemberCategory.INVOKE_DECLARED_CONSTRUCTORS,
                    MemberCategory.INVOKE_PUBLIC_CONSTRUCTORS,
                    MemberCategory.INVOKE_DECLARED_METHODS,
                    MemberCategory.INVOKE_PUBLIC_METHODS,
                    MemberCategory.DECLARED_FIELDS,
                    MemberCategory.PUBLIC_FIELDS
            );

            // Register Spring Data Unpaged class (required by Spring Data's PageModule)
            // This is needed even though we don't use pagination, because Spring Data JPA
            // auto-configures Jackson's PageModule
            try {
                Class<?> unpagedClass = Class.forName("org.springframework.data.domain.Unpaged");
                hints.reflection().registerType(
                        unpagedClass,
                        MemberCategory.INVOKE_DECLARED_CONSTRUCTORS,
                        MemberCategory.INVOKE_PUBLIC_CONSTRUCTORS,
                        MemberCategory.INVOKE_DECLARED_METHODS,
                        MemberCategory.INVOKE_PUBLIC_METHODS,
                        MemberCategory.DECLARED_FIELDS,
                        MemberCategory.PUBLIC_FIELDS
                );
            } catch (ClassNotFoundException e) {
                // Unpaged might not exist in older Spring Data versions
            }
        }
    }
}
