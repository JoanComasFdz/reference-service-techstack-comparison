package com.joancomasfdz.performancetest;

import jakarta.persistence.*;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;
import org.springframework.transaction.annotation.Transactional;

import java.time.Instant;

/**
 * JPA entity representing an instrument status record.
 * This entity stores historical status change data for instruments,
 * including device information and status transitions.
 */
@Entity
@Table(name = "java21springboot_instrument_status", indexes = {
    @Index(name = "idx_java21springboot_instrument_status_timestamp", columnList = "timestamp DESC")
})
public class InstrumentStatus {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Integer id;

    @Column(name = "device_id", nullable = false)
    private String deviceId;

    @Column(name = "previous_status", nullable = false)
    private String previousStatus;

    @Column(name = "current_status", nullable = false)
    private String currentStatus;

    @Column(nullable = false)
    private Instant timestamp;

    /**
     * Default constructor for JPA.
     */
    public InstrumentStatus() {
    }

    /**
     * Constructs a new InstrumentStatus with all required fields.
     *
     * @param deviceId the unique identifier of the device
     * @param previousStatus the status before the change
     * @param currentStatus the status after the change
     * @param timestamp the time when the status change occurred
     */
    public InstrumentStatus(String deviceId, String previousStatus, String currentStatus, Instant timestamp) {
        this.deviceId = deviceId;
        this.previousStatus = previousStatus;
        this.currentStatus = currentStatus;
        this.timestamp = timestamp;
    }

    /**
     * Gets the unique identifier of this record.
     *
     * @return the record ID
     */
    public Integer getId() {
        return id;
    }

    /**
     * Sets the unique identifier of this record.
     *
     * @param id the record ID
     */
    public void setId(Integer id) {
        this.id = id;
    }

    /**
     * Gets the device identifier.
     *
     * @return the device ID
     */
    public String getDeviceId() {
        return deviceId;
    }

    /**
     * Sets the device identifier.
     *
     * @param deviceId the device ID
     */
    public void setDeviceId(String deviceId) {
        this.deviceId = deviceId;
    }

    /**
     * Gets the previous status before the change.
     *
     * @return the previous status
     */
    public String getPreviousStatus() {
        return previousStatus;
    }

    /**
     * Sets the previous status before the change.
     *
     * @param previousStatus the previous status
     */
    public void setPreviousStatus(String previousStatus) {
        this.previousStatus = previousStatus;
    }

    /**
     * Gets the current status after the change.
     *
     * @return the current status
     */
    public String getCurrentStatus() {
        return currentStatus;
    }

    /**
     * Sets the current status after the change.
     *
     * @param currentStatus the current status
     */
    public void setCurrentStatus(String currentStatus) {
        this.currentStatus = currentStatus;
    }

    /**
     * Gets the timestamp when the status change occurred.
     *
     * @return the timestamp
     */
    public Instant getTimestamp() {
        return timestamp;
    }

    /**
     * Sets the timestamp when the status change occurred.
     *
     * @param timestamp the timestamp
     */
    public void setTimestamp(Instant timestamp) {
        this.timestamp = timestamp;
    }
}

/**
 * Repository interface for InstrumentStatus entity.
 * Provides database access methods for instrument status records.
 */
@Repository
interface InstrumentStatusRepository extends JpaRepository<InstrumentStatus, Integer> {

    /**
     * Finds the most recent instrument status record based on timestamp.
     * This method uses a read-only transaction for optimized query performance,
     * as it does not modify the entity state or require change tracking.
     *
     * @return the latest instrument status record, or null if no records exist
     */
    @Transactional(readOnly = true)
    InstrumentStatus findFirstByOrderByTimestampDesc();
}
