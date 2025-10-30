package com.joancomasfdz.performancetest;

import io.quarkus.hibernate.orm.panache.PanacheRepository;
import io.quarkus.panache.common.Sort;
import jakarta.enterprise.context.ApplicationScoped;

/**
 * Repository for accessing and managing InstrumentStatus entities.
 *
 * This repository extends PanacheRepository to provide convenient data access methods
 * for instrument status records stored in the database.
 */
@ApplicationScoped
public class InstrumentStatusRepository implements PanacheRepository<InstrumentStatus> {

    /**
     * Retrieves the most recent instrument status record based on timestamp.
     *
     * This method queries all instrument status records and returns the one with
     * the latest timestamp. Returns null if no records exist in the database.
     *
     * @return the most recent InstrumentStatus entity, or null if no records exist
     */
    public InstrumentStatus findLatest() {
        return findAll(Sort.descending("timestamp")).firstResult();
    }
}
