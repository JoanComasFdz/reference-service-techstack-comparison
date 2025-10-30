package com.joancomasfdz.performancetest;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * REST endpoint for exposing Key Performance Indicator (KPI) data.
 * This controller provides access to the latest instrument status information
 * via HTTP GET requests.
 *
 * @author Performance Test 2025
 * @version 1.0
 */
@RestController
public class KpiEndpoint {

    private static final Logger log = LoggerFactory.getLogger(KpiEndpoint.class);

    private final InstrumentStatusRepository repository;

    /**
     * Constructs a new KpiEndpoint with the required repository dependency.
     *
     * @param repository the repository for accessing instrument status data
     */
    public KpiEndpoint(InstrumentStatusRepository repository) {
        this.repository = repository;
    }

    /**
     * Retrieves the most recent instrument status KPI.
     * This endpoint returns the latest status change event for instruments
     * in the system, ordered by timestamp.
     *
     * @return the latest {@link InstrumentStatus} record, or null if no records exist
     */
    @GetMapping("/kpi")
    public InstrumentStatus getLatestKpi() {
        log.info("GET /kpi - Fetching latest instrument status");
        InstrumentStatus status = repository.findFirstByOrderByTimestampDesc();
        log.info("Returning latest instrument status record");
        return status;
    }
}
