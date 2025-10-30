package com.joancomasfdz.performancetest;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * REST controller that provides KPI endpoints for instrument status data.
 * Exposes HTTP endpoints for retrieving the latest instrument status information.
 */
@RestController
public class KpiEndpoint {

    private static final Logger log = LoggerFactory.getLogger(KpiEndpoint.class);

    private final InstrumentStatusRepository repository;

    /**
     * Constructs a new KpiEndpoint.
     *
     * @param repository the repository for accessing instrument status records
     */
    public KpiEndpoint(InstrumentStatusRepository repository) {
        this.repository = repository;
    }

    /**
     * Retrieves the most recent instrument status record.
     * This endpoint returns the latest status change event that was processed and stored.
     *
     * @return the latest instrument status record, or null if no records exist
     */
    @GetMapping("/kpi")
    public InstrumentStatus getLatestInstrumentStatus() {
        log.info("GET /kpi - Fetching latest instrument status");
        InstrumentStatus status = repository.findFirstByOrderByTimestampDesc();
        log.info("Returning latest instrument status record");
        return status;
    }
}
