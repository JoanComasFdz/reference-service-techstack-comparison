package com.joancomasfdz.performancetest;

import io.quarkus.runtime.annotations.RegisterForReflection;
import com.joancomasfdz.java25.events.CloudEvent;
import com.joancomasfdz.java25.events.InstrumentStatusChangedEvent;
import com.joancomasfdz.java25.events.InstrumentStatusKpiUpdatedEvent;

/**
 * GraalVM Native Image Reflection Configuration
 *
 * This class registers event classes for reflection so that Jackson can deserialize
 * JSON messages into these classes in native image mode.
 *
 * Without this registration, Jackson cannot:
 * - Call default constructors
 * - Access setter methods
 * - Deserialize JSON into these POJOs
 *
 * The @RegisterForReflection annotation tells Quarkus/GraalVM to include reflection
 * metadata for these classes in the native image.
 */
@RegisterForReflection(targets = {
    CloudEvent.class,
    InstrumentStatusChangedEvent.class,
    InstrumentStatusKpiUpdatedEvent.class
})
public class ReflectionConfiguration {

    private ReflectionConfiguration() {
        // Private constructor to prevent instantiation
    }
}
