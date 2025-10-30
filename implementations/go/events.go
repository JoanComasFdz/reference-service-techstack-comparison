package main

import (
	"time"

	"github.com/google/uuid"
)

const (
	EventTypeInstrumentStatusChanged = "instrument.status.changed"
	EventTypeKpiUpdated              = "instrumentstatus.kpi.updated"
)

// CloudEvent represents a CloudEvents v1.0 compliant event with extensions
type CloudEvent struct {
	ID              string                 `json:"id"`
	SpecVersion     string                 `json:"specversion"`
	Source          string                 `json:"source"`
	Type            string                 `json:"type"`
	Time            string                 `json:"time"`
	PrivacyRelevant bool                   `json:"privacyrelevant"`
	DataContentType string                 `json:"datacontenttype"`
	DataSchema      string                 `json:"dataschema"`
	Kind            string                 `json:"kind"`
	Data            map[string]interface{} `json:"data"`
}

// InstrumentStatusChangedEvent represents an event when instrument status changes
type InstrumentStatusChangedEvent struct {
	CloudEvent
}

// GetDeviceID extracts the device ID from the event data
func (e *InstrumentStatusChangedEvent) GetDeviceID() string {
	if e.Data == nil {
		return ""
	}
	if deviceID, ok := e.Data["deviceId"].(string); ok {
		return deviceID
	}
	return ""
}

// GetPreviousStatus extracts the previous status from the event data
func (e *InstrumentStatusChangedEvent) GetPreviousStatus() string {
	if e.Data == nil {
		return ""
	}
	if previousStatus, ok := e.Data["previousStatus"].(string); ok {
		return previousStatus
	}
	return ""
}

// GetCurrentStatus extracts the current status from the event data
func (e *InstrumentStatusChangedEvent) GetCurrentStatus() string {
	if e.Data == nil {
		return ""
	}
	if currentStatus, ok := e.Data["currentStatus"].(string); ok {
		return currentStatus
	}
	return ""
}

// InstrumentStatusKpiUpdatedEvent represents a KPI update event
type InstrumentStatusKpiUpdatedEvent struct {
	CloudEvent
}

// NewInstrumentStatusKpiUpdatedEvent creates a new InstrumentStatusKpiUpdatedEvent
func NewInstrumentStatusKpiUpdatedEvent(source, deviceID, currentStatus string) *InstrumentStatusKpiUpdatedEvent {
	return &InstrumentStatusKpiUpdatedEvent{
		CloudEvent: CloudEvent{
			ID:              uuid.New().String(),
			SpecVersion:     "1.0",
			Source:          source,
			Type:            EventTypeKpiUpdated,
			Time:            time.Now().UTC().Format(time.RFC3339),
			PrivacyRelevant: false,
			DataContentType: "application/json",
			DataSchema:      "https://schemas.joancomasfdz.com/schemas/events-catalog/instrumentstatus.kpi.updated.schema.json",
			Kind:            "event",
			Data: map[string]interface{}{
				"deviceId":      deviceID,
				"currentStatus": currentStatus,
			},
		},
	}
}

// GetDeviceID extracts the device ID from the event data
func (e *InstrumentStatusKpiUpdatedEvent) GetDeviceID() string {
	if e.Data == nil {
		return ""
	}
	if deviceID, ok := e.Data["deviceId"].(string); ok {
		return deviceID
	}
	return ""
}

// GetCurrentStatus extracts the current status from the event data
func (e *InstrumentStatusKpiUpdatedEvent) GetCurrentStatus() string {
	if e.Data == nil {
		return ""
	}
	if currentStatus, ok := e.Data["currentStatus"].(string); ok {
		return currentStatus
	}
	return ""
}
