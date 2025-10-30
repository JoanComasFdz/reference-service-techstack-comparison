use crate::database::save_instrument_status;
use crate::models::{InstrumentStatusChangedEvent, InstrumentStatusKpiUpdatedEvent};
use crate::rabbitmq::{publish_message, ROUTING_KEY_KPI_UPDATED};
use anyhow::Result;
use lapin::Channel;
use sea_orm::DatabaseConnection;

const SERVICE_SOURCE_URN: &str = "urn:uuid:rust-actix";

/// Handle incoming instrument.status.changed event
pub async fn handle_status_changed_event(
    db: &DatabaseConnection,
    rabbitmq_channel: &Channel,
    event_json: &str,
) -> Result<()> {
    // Parse the incoming CloudEvents v1.0 event
    let event: InstrumentStatusChangedEvent = serde_json::from_str(event_json)?;

    log::info!(
        "Received event: {event_type} from {source}",
        event_type = event.event_type,
        source = event.source
    );

    let device_id = event.get_device_id();
    let previous_status = event.get_previous_status();
    let current_status = event.get_current_status();

    log::info!(
        "Processing status change for device: {device_id} from {previous_status} to {current_status}",
        device_id = device_id,
        previous_status = previous_status,
        current_status = current_status
    );

    // Save instrument status to database using SeaORM
    save_instrument_status(db, device_id, previous_status, current_status).await?;

    // Publish KPI event
    publish_kpi_event(rabbitmq_channel, device_id, current_status).await?;

    Ok(())
}

/// Publish instrumentstatus.kpi.updated event
async fn publish_kpi_event(channel: &Channel, device_id: &str, current_status: &str) -> Result<()> {
    // Create the KPI event
    let kpi_event = InstrumentStatusKpiUpdatedEvent::new(
        SERVICE_SOURCE_URN.to_string(),
        device_id.to_string(),
        current_status.to_string(),
    );

    // Serialize to JSON
    let kpi_event_json = serde_json::to_string(&kpi_event)?;

    // Publish to RabbitMQ
    publish_message(channel, ROUTING_KEY_KPI_UPDATED, &kpi_event_json).await?;

    log::info!("Published KPI event for device: {device_id}");

    Ok(())
}
