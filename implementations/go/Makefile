.PHONY: help build run clean test deps

help:
	@echo "Go Reference Service - Performance Test 2025"
	@echo ""
	@echo "Available targets:"
	@echo "  deps    - Download dependencies"
	@echo "  build   - Build the service"
	@echo "  run     - Run the service"
	@echo "  clean   - Remove build artifacts"
	@echo "  test    - Run tests"

deps:
	@echo "Downloading dependencies..."
	go mod download

build: deps
	@echo "Building Go service..."
	go build -o goReferenceService .
	@echo "Build complete: ./goReferenceService"

build-optimized: deps
	@echo "Building Go service with optimizations..."
	go build -ldflags="-s -w" -o goReferenceService .
	@echo "Build complete: ./goReferenceService"

run: build
	@echo "Starting Go service..."
	./goReferenceService

clean:
	@echo "Cleaning build artifacts..."
	rm -f goReferenceService
	@echo "Clean complete"

test:
	@echo "Running tests..."
	go test -v ./...
