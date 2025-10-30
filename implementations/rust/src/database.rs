use crate::models::{ActiveModel, Entity as InstrumentStatusEntity, InstrumentStatus};
use anyhow::Result;
use chrono::Utc;
use sea_orm::{
    ActiveModelTrait, ActiveValue, ConnectionTrait, Database, DatabaseConnection, DbBackend,
    EntityTrait, QueryOrder, Statement,
};

/// Initialize the database connection
pub async fn create_pool(database_url: &str) -> Result<DatabaseConnection> {
    let db = Database::connect(database_url).await?;
    log::info!("Database connection pool created successfully");
    Ok(db)
}

/// Initialize the database schema (create table if it doesn't exist)
pub async fn initialize_schema(db: &DatabaseConnection) -> Result<()> {
    // Create table using raw SQL to match existing schema
    let create_table_sql = r#"
        CREATE TABLE IF NOT EXISTS rust_instrument_status (
            id SERIAL PRIMARY KEY,
            device_id VARCHAR(255) NOT NULL,
            previous_status VARCHAR(255) NOT NULL,
            current_status VARCHAR(255) NOT NULL,
            timestamp TIMESTAMPTZ NOT NULL
        )
    "#;

    db.execute(Statement::from_string(
        DbBackend::Postgres,
        create_table_sql.to_owned(),
    ))
    .await?;

    // Create index
    let create_index_sql = r#"
        CREATE INDEX IF NOT EXISTS idx_rust_instrument_status_timestamp
        ON rust_instrument_status (timestamp DESC)
    "#;

    db.execute(Statement::from_string(
        DbBackend::Postgres,
        create_index_sql.to_owned(),
    ))
    .await?;

    log::info!("Database schema initialized");
    Ok(())
}

/// Save instrument status to the database
pub async fn save_instrument_status(
    db: &DatabaseConnection,
    device_id: &str,
    previous_status: &str,
    current_status: &str,
) -> Result<i32> {
    let timestamp = Utc::now();

    let instrument_status = ActiveModel {
        device_id: ActiveValue::Set(device_id.to_string()),
        previous_status: ActiveValue::Set(previous_status.to_string()),
        current_status: ActiveValue::Set(current_status.to_string()),
        timestamp: ActiveValue::Set(timestamp.into()),
        ..Default::default()
    };

    let result = instrument_status.insert(db).await?;
    log::info!(
        "Saved instrument status to database with ID: {id}",
        id = result.id
    );
    Ok(result.id)
}

/// Get the latest instrument status from the database
pub async fn get_latest_instrument_status(
    db: &DatabaseConnection,
) -> Result<Option<InstrumentStatus>> {
    let result = InstrumentStatusEntity::find()
        .order_by_desc(crate::models::Column::Timestamp)
        .one(db)
        .await?;

    Ok(result)
}
