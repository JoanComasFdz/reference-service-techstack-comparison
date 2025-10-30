mod api;
mod database;
mod handlers;
mod models;
mod rabbitmq;

use actix_web::{web, App, HttpServer};
use anyhow::Result;
use futures_util::stream::StreamExt;
use lapin::{
    options::{BasicAckOptions, BasicConsumeOptions, BasicNackOptions},
    types::FieldTable,
};
use std::env;

#[actix_web::main]
async fn main() -> Result<()> {
    // Load environment variables
    dotenv::dotenv().ok();

    // Initialize logger
    env_logger::init_from_env(env_logger::Env::new().default_filter_or("info"));

    log::info!("Starting Rust Actix Reference Service...");

    // Read configuration from environment variables
    let database_url = env::var("DATABASE_URL")
        .unwrap_or_else(|_| "postgresql://admin:admin@localhost:5432/rust_db".to_string());
    let rabbitmq_url = env::var("RABBITMQ_URL")
        .unwrap_or_else(|_| "amqp://admin:admin@localhost:5672".to_string());
    let rabbitmq_prefetch_count: u16 = env::var("RABBITMQ_PREFETCH_COUNT")
        .unwrap_or_else(|_| "50".to_string())
        .parse()
        .unwrap_or(50);
    let server_host = env::var("SERVER_HOST").unwrap_or_else(|_| "0.0.0.0".to_string());
    let server_port = env::var("SERVER_PORT").unwrap_or_else(|_| "8100".to_string());

    // Initialize database
    log::info!("Connecting to database: {database_url}");
    let db_pool = database::create_pool(&database_url).await?;
    database::initialize_schema(&db_pool).await?;

    // Initialize RabbitMQ
    log::info!("Connecting to RabbitMQ: {rabbitmq_url}");
    let rabbitmq_conn = rabbitmq::create_connection(&rabbitmq_url).await?;
    let rabbitmq_channel = rabbitmq::setup_channel(&rabbitmq_conn, rabbitmq_prefetch_count).await?;

    // Clone resources for the consumer task
    let db_pool_clone = db_pool.clone();
    let channel_clone = rabbitmq_channel.clone();

    // Start RabbitMQ consumer in a background task
    tokio::spawn(async move {
        if let Err(e) = start_consumer(db_pool_clone, channel_clone).await {
            log::error!("Consumer error: {e}");
        }
    });

    // Start HTTP server
    let app_state = web::Data::new(api::AppState {
        db_pool: db_pool.clone(),
    });

    let server_address = format!("{}:{}", server_host, server_port);
    log::info!("Starting HTTP server on {server_address}");

    HttpServer::new(move || {
        App::new()
            .app_data(app_state.clone())
            .configure(api::configure_routes)
    })
    .bind(&server_address)?
    .run()
    .await?;

    Ok(())
}

/// Start RabbitMQ consumer
async fn start_consumer(
    db_pool: sea_orm::DatabaseConnection,
    channel: lapin::Channel,
) -> Result<()> {
    log::info!(
        "Starting RabbitMQ consumer for queue: {queue_name}",
        queue_name = rabbitmq::QUEUE_NAME
    );

    let mut consumer = channel
        .basic_consume(
            rabbitmq::QUEUE_NAME,
            "rust-actix-consumer",
            BasicConsumeOptions::default(),
            FieldTable::default(),
        )
        .await?;

    log::info!("RabbitMQ consumer started successfully");

    while let Some(delivery) = consumer.next().await {
        match delivery {
            Ok(delivery) => {
                let message = String::from_utf8_lossy(&delivery.data);
                log::debug!("Received message: {message}");

                // Process the event
                match handlers::handle_status_changed_event(&db_pool, &channel, &message).await {
                    Ok(_) => {
                        // Acknowledge the message
                        if let Err(e) = delivery.ack(BasicAckOptions::default()).await {
                            log::error!("Failed to acknowledge message: {e}");
                        }
                    }
                    Err(e) => {
                        log::error!("Error processing message: {e}");
                        // Reject and requeue the message
                        if let Err(e) = delivery
                            .nack(BasicNackOptions {
                                requeue: true,
                                multiple: false,
                            })
                            .await
                        {
                            log::error!("Failed to nack message: {e}");
                        }
                    }
                }
            }
            Err(e) => {
                log::error!("Consumer delivery error: {e}");
            }
        }
    }

    Ok(())
}
