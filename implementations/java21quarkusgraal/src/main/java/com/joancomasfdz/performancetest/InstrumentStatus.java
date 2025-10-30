package com.joancomasfdz.performancetest;

import jakarta.persistence.*;

import java.time.Instant;

/**
 * JPA Entity representing an instrument status record.
 *
 * This entity stores historical status changes for instruments, tracking transitions
 * from previous to current states with timestamps. Records are indexed by timestamp
 * in descending order for efficient retrieval of recent status changes.
 *
 * Note: This class uses Panache pattern with public fields for direct field access.
 * This is the recommended approach in Quarkus and should not be changed to follow
 * traditional JavaBean patterns.
 */
@Entity
@Table(name = "java21quarkusgraal_instrument_status", indexes = {
    @Index(name = "idx_java21quarkusgraal_instrument_status_timestamp", columnList = "timestamp")
})
public class InstrumentStatus {

    /**
     * Primary key - auto-generated database ID.
     */
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    public Integer id;

    /**
     * Unique identifier of the instrument/device.
     */
    @Column(name = "device_id", nullable = false)
    public String deviceId;

    /**
     * The status the instrument was transitioning from.
     */
    @Column(name = "previous_status", nullable = false)
    public String previousStatus;

    /**
     * The new status the instrument transitioned to.
     */
    @Column(name = "current_status", nullable = false)
    public String currentStatus;

    /**
     * Timestamp when the status record was created.
     */
    @Column(nullable = false)
    public Instant timestamp;

    /**
     * Default no-argument constructor required by JPA.
     */
    public InstrumentStatus() {
    }

    /**
     * Creates a new instrument status record.
     *
     * @param deviceId the unique identifier of the instrument
     * @param previousStatus the status the instrument was transitioning from
     * @param currentStatus the new status the instrument transitioned to
     * @param timestamp the time when this status change occurred
     */
    public InstrumentStatus(String deviceId, String previousStatus, String currentStatus, Instant timestamp) {
        this.deviceId = deviceId;
        this.previousStatus = previousStatus;
        this.currentStatus = currentStatus;
        this.timestamp = timestamp;
    }
}
