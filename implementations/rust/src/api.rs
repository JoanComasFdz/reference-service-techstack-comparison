use crate::database::get_latest_instrument_status;
use actix_web::{web, HttpResponse, Responder};
use sea_orm::DatabaseConnection;

/// Application state shared across handlers
pub struct AppState {
    pub db_pool: DatabaseConnection,
}

/// GET /kpi endpoint - returns the latest instrument status
pub async fn get_kpi(data: web::Data<AppState>) -> impl Responder {
    log::info!("GET /kpi - Fetching latest instrument status");

    match get_latest_instrument_status(&data.db_pool).await {
        Ok(Some(status)) => {
            log::info!("Returning latest instrument status record");
            HttpResponse::Ok().json(status)
        }
        Ok(None) => {
            log::info!("No instrument status records found");
            HttpResponse::NotFound().json(serde_json::json!({
                "error": "No instrument status records found"
            }))
        }
        Err(e) => {
            log::error!("Error fetching latest instrument status: {e}");
            HttpResponse::InternalServerError().json(serde_json::json!({
                "error": "Failed to fetch latest instrument status"
            }))
        }
    }
}

/// Configure API routes
pub fn configure_routes(cfg: &mut web::ServiceConfig) {
    cfg.service(web::resource("/kpi").route(web::get().to(get_kpi)));
}
