package main

import (
	"fmt"
	"log"
	"time"

	"gorm.io/driver/postgres"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
)

// InstrumentStatus represents the database model for instrument status
type InstrumentStatus struct {
	ID             int32     `gorm:"primaryKey;autoIncrement" json:"id"`
	DeviceID       string    `gorm:"column:device_id;type:varchar(255);not null" json:"deviceId"`
	PreviousStatus string    `gorm:"column:previous_status;type:varchar(255);not null" json:"previousStatus"`
	CurrentStatus  string    `gorm:"column:current_status;type:varchar(255);not null" json:"currentStatus"`
	Timestamp      time.Time `gorm:"column:timestamp;type:timestamptz;not null;index:idx_go_instrument_status_timestamp,sort:desc" json:"timestamp"`
}

// TableName specifies the table name for GORM
func (InstrumentStatus) TableName() string {
	return "go_instrument_status"
}

// Database handles all database operations
type Database struct {
	db *gorm.DB
}

// NewDatabase creates a new Database connection using GORM
func NewDatabase(config *Config) (*Database, error) {
	dsn := fmt.Sprintf(
		"host=%s port=%d user=%s password=%s dbname=%s sslmode=disable",
		config.DBHost,
		config.DBPort,
		config.DBUser,
		config.DBPassword,
		config.DBName,
	)

	db, err := gorm.Open(postgres.Open(dsn), &gorm.Config{
		Logger: logger.Default.LogMode(logger.Silent),
	})
	if err != nil {
		return nil, fmt.Errorf("failed to connect to database: %w", err)
	}

	// Get underlying SQL database for ping
	sqlDB, err := db.DB()
	if err != nil {
		return nil, fmt.Errorf("failed to get database instance: %w", err)
	}

	// Test the connection
	if err := sqlDB.Ping(); err != nil {
		return nil, fmt.Errorf("failed to ping database: %w", err)
	}

	database := &Database{db: db}

	// Auto-migrate the schema
	if err := database.autoMigrate(); err != nil {
		return nil, fmt.Errorf("failed to migrate schema: %w", err)
	}

	log.Println("Database connection established with GORM")
	return database, nil
}

// autoMigrate automatically creates/updates the table schema
func (d *Database) autoMigrate() error {
	err := d.db.AutoMigrate(&InstrumentStatus{})
	if err != nil {
		return fmt.Errorf("failed to auto-migrate: %w", err)
	}

	log.Println("Table schema migrated successfully")
	return nil
}

// SaveInstrumentStatus saves an instrument status record to the database
func (d *Database) SaveInstrumentStatus(deviceID, previousStatus, currentStatus string) (*InstrumentStatus, error) {
	status := &InstrumentStatus{
		DeviceID:       deviceID,
		PreviousStatus: previousStatus,
		CurrentStatus:  currentStatus,
		Timestamp:      time.Now().UTC(),
	}

	result := d.db.Create(status)
	if result.Error != nil {
		return nil, fmt.Errorf("failed to save instrument status: %w", result.Error)
	}

	log.Printf("Saved instrument status to database with ID: %d", status.ID)
	return status, nil
}

// GetLatestInstrumentStatus retrieves the latest instrument status from the database
func (d *Database) GetLatestInstrumentStatus() (*InstrumentStatus, error) {
	var status InstrumentStatus

	result := d.db.Order("timestamp DESC").First(&status)

	if result.Error == gorm.ErrRecordNotFound {
		return nil, nil
	}

	if result.Error != nil {
		return nil, fmt.Errorf("failed to get latest instrument status: %w", result.Error)
	}

	return &status, nil
}

// Close closes the database connection
func (d *Database) Close() error {
	sqlDB, err := d.db.DB()
	if err != nil {
		return err
	}
	return sqlDB.Close()
}
