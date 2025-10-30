package main

import (
	"encoding/json"
	"fmt"
	"log"
	"net/http"

	"github.com/gorilla/mux"
)

// Server handles HTTP requests
type Server struct {
	db     *Database
	router *mux.Router
	config *Config
}

// NewServer creates a new HTTP server
func NewServer(config *Config, db *Database) *Server {
	server := &Server{
		db:     db,
		router: mux.NewRouter(),
		config: config,
	}

	server.setupRoutes()
	return server
}

// setupRoutes configures the HTTP routes
func (s *Server) setupRoutes() {
	s.router.HandleFunc("/kpi", s.handleGetKpi).Methods("GET")
}

// handleGetKpi handles GET /kpi requests
func (s *Server) handleGetKpi(w http.ResponseWriter, r *http.Request) {
	log.Println("GET /kpi - Fetching latest instrument status")

	status, err := s.db.GetLatestInstrumentStatus()
	if err != nil {
		log.Printf("Error fetching latest status: %v", err)
		http.Error(w, "Internal server error", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")

	if status == nil {
		// Return empty object if no records exist
		if err := json.NewEncoder(w).Encode(map[string]interface{}{}); err != nil {
			log.Printf("Error encoding empty response: %v", err)
			http.Error(w, "Failed to encode response", http.StatusInternalServerError)
			return
		}
		return
	}

	log.Println("Returning latest instrument status record")
	if err := json.NewEncoder(w).Encode(status); err != nil {
		log.Printf("Error encoding status response: %v", err)
		http.Error(w, "Failed to encode response", http.StatusInternalServerError)
		return
	}
}

// Start starts the HTTP server
func (s *Server) Start() error {
	addr := fmt.Sprintf(":%d", s.config.ServerPort)
	log.Printf("HTTP server listening on %s", addr)
	return http.ListenAndServe(addr, s.router)
}
