package com.joancomasfdz.performancetest;

import jakarta.inject.Inject;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import org.jboss.logging.Logger;

/**
 * REST endpoint for retrieving instrument status KPI data.
 *
 * This endpoint provides HTTP access to the most recent instrument status information,
 * which serves as a Key Performance Indicator (KPI) for monitoring instrument health.
 */
@Path("/kpi")
public class KpiEndpoint {

    private static final Logger LOG = Logger.getLogger(KpiEndpoint.class);

    @Inject
    InstrumentStatusRepository repository;

    /**
     * Retrieves the latest instrument status record.
     *
     * This endpoint returns the most recent instrument status change that was
     * processed and stored in the database. The status information includes
     * device ID, previous status, current status, and timestamp.
     *
     * @return the most recent InstrumentStatus as JSON, or null if no records exist
     */
    @GET
    @Produces(MediaType.APPLICATION_JSON)
    public InstrumentStatus getLatestKpi() {
        LOG.info("GET /kpi - Fetching latest instrument status");
        InstrumentStatus status = repository.findLatest();
        LOG.info("Returning latest instrument status record");
        return status;
    }
}
