use chrono::Utc;
use sea_orm::entity::prelude::*;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// Database entity representing instrument status
#[derive(Clone, Debug, PartialEq, DeriveEntityModel, Serialize, Deserialize)]
#[sea_orm(table_name = "rust_instrument_status")]
pub struct Model {
    #[sea_orm(primary_key)]
    pub id: i32,
    #[serde(rename = "deviceId")]
    pub device_id: String,
    #[serde(rename = "previousStatus")]
    pub previous_status: String,
    #[serde(rename = "currentStatus")]
    pub current_status: String,
    pub timestamp: DateTimeWithTimeZone,
}

#[derive(Copy, Clone, Debug, EnumIter, DeriveRelation)]
pub enum Relation {}

impl ActiveModelBehavior for ActiveModel {}

/// Alias for backwards compatibility
pub type InstrumentStatus = Model;

/// CloudEvents v1.0 event representing a change in instrument status
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstrumentStatusChangedEvent {
    pub id: String,
    pub specversion: String,
    pub source: String,
    #[serde(rename = "type")]
    pub event_type: String,
    pub time: String,
    pub privacyrelevant: bool,
    pub datacontenttype: String,
    pub dataschema: String,
    pub kind: String,
    pub data: InstrumentStatusChangedData,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstrumentStatusChangedData {
    #[serde(rename = "deviceId")]
    pub device_id: String,
    #[serde(rename = "previousStatus")]
    pub previous_status: String,
    #[serde(rename = "currentStatus")]
    pub current_status: String,
}

impl InstrumentStatusChangedEvent {
    pub fn get_device_id(&self) -> &str {
        &self.data.device_id
    }

    pub fn get_previous_status(&self) -> &str {
        &self.data.previous_status
    }

    pub fn get_current_status(&self) -> &str {
        &self.data.current_status
    }
}

/// CloudEvents v1.0 event representing a KPI update for instrument status
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstrumentStatusKpiUpdatedEvent {
    pub id: String,
    pub specversion: String,
    pub source: String,
    #[serde(rename = "type")]
    pub event_type: String,
    pub time: String,
    pub privacyrelevant: bool,
    pub datacontenttype: String,
    pub dataschema: String,
    pub kind: String,
    pub data: InstrumentStatusKpiUpdatedData,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstrumentStatusKpiUpdatedData {
    #[serde(rename = "deviceId")]
    pub device_id: String,
    #[serde(rename = "currentStatus")]
    pub current_status: String,
}

impl InstrumentStatusKpiUpdatedEvent {
    pub fn new(source: String, device_id: String, current_status: String) -> Self {
        Self {
            id: Uuid::new_v4().to_string(),
            specversion: "1.0".to_string(),
            source,
            event_type: "instrumentstatus.kpi.updated".to_string(),
            time: Utc::now().to_rfc3339(),
            privacyrelevant: false,
            datacontenttype: "application/json".to_string(),
            dataschema: "https://example.com/schemas/events-catalog/instrumentstatus.kpi.updated.schema.json".to_string(),
            kind: "event".to_string(),
            data: InstrumentStatusKpiUpdatedData {
                device_id,
                current_status,
            },
        }
    }
}
