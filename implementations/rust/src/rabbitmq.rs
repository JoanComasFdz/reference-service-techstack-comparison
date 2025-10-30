use anyhow::Result;
use lapin::{
    options::{
        BasicPublishOptions, BasicQosOptions, ExchangeDeclareOptions, QueueBindOptions,
        QueueDeclareOptions,
    },
    types::FieldTable,
    BasicProperties, Channel, Connection, ConnectionProperties, ExchangeKind,
};

pub const EXCHANGE_NAME: &str = "referenceservice.comparison";
pub const QUEUE_NAME: &str = "rustActix";
pub const ROUTING_KEY_STATUS_CHANGED: &str = "instrument.status.changed";
pub const ROUTING_KEY_KPI_UPDATED: &str = "instrumentstatus.kpi.updated";

/// Create a RabbitMQ connection
pub async fn create_connection(rabbitmq_url: &str) -> Result<Connection> {
    let conn = Connection::connect(rabbitmq_url, ConnectionProperties::default()).await?;
    log::info!("RabbitMQ connection established");
    Ok(conn)
}

/// Create a RabbitMQ channel and set up exchange, queue, and binding
pub async fn setup_channel(conn: &Connection, prefetch_count: u16) -> Result<Channel> {
    let channel = conn.create_channel().await?;

    // Declare the exchange
    channel
        .exchange_declare(
            EXCHANGE_NAME,
            ExchangeKind::Topic,
            ExchangeDeclareOptions {
                durable: true,
                auto_delete: false,
                internal: false,
                nowait: false,
                passive: false,
            },
            FieldTable::default(),
        )
        .await?;
    log::info!("Declared exchange: {EXCHANGE_NAME}");

    // Declare the queue
    channel
        .queue_declare(
            QUEUE_NAME,
            QueueDeclareOptions {
                durable: true,
                exclusive: false,
                auto_delete: false,
                nowait: false,
                passive: false,
            },
            FieldTable::default(),
        )
        .await?;
    log::info!("Declared queue: {QUEUE_NAME}");

    // Bind the queue to the exchange with the routing key
    channel
        .queue_bind(
            QUEUE_NAME,
            EXCHANGE_NAME,
            ROUTING_KEY_STATUS_CHANGED,
            QueueBindOptions::default(),
            FieldTable::default(),
        )
        .await?;
    log::info!(
        "Bound queue {QUEUE_NAME} to exchange {EXCHANGE_NAME} with routing key {ROUTING_KEY_STATUS_CHANGED}"
    );

    // Set QoS prefetch count
    channel
        .basic_qos(prefetch_count, BasicQosOptions::default())
        .await?;
    log::info!("Set QoS prefetch count to {prefetch_count}");

    Ok(channel)
}

/// Publish a message to RabbitMQ
pub async fn publish_message(channel: &Channel, routing_key: &str, message: &str) -> Result<()> {
    channel
        .basic_publish(
            EXCHANGE_NAME,
            routing_key,
            BasicPublishOptions::default(),
            message.as_bytes(),
            BasicProperties::default()
                .with_content_type("application/json".into())
                .with_delivery_mode(2), // persistent
        )
        .await?
        .await?;

    log::debug!("Published message to {routing_key}: {message}");
    Ok(())
}
