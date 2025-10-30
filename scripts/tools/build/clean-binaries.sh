#!/bin/bash

# Script to remove all binaries and build artifacts from service reference implementations
# This script cleans compiled code, build directories, and other generated artifacts

set -e  # Exit on error

# Function to get directory size in KB
get_size() {
    local dir=$1
    if [ -d "$dir" ]; then
        du -sk "$dir" 2>/dev/null | awk '{print $1}'
    else
        echo "0"
    fi
}

# Function to format size in human-readable format
format_size() {
    local size_kb=$1
    if [ "$size_kb" -ge 1048576 ]; then
        echo "$(awk "BEGIN {printf \"%.2f GB\", $size_kb/1048576}")"
    elif [ "$size_kb" -ge 1024 ]; then
        echo "$(awk "BEGIN {printf \"%.2f MB\", $size_kb/1024}")"
    else
        echo "${size_kb} KB"
    fi
}

# Array to store service names and sizes
declare -a services=("implementations/bun" "implementations/dotnet9" "implementations/dotnet9.events" "implementations/dotnet9aot" "implementations/go" "implementations/java21.events" "implementations/java21quarkusgraal" "implementations/java21springboot" "implementations/java21springbootgraal" "implementations/java25.events" "implementations/java25quarkusgraal" "implementations/java25springboot" "implementations/java25springbootgraal" "implementations/python" "implementations/rust")
declare -A before_sizes
declare -A after_sizes

echo "🧹 Cleaning binaries and build artifacts from all service implementations..."
echo ""
echo "📊 Taking initial size snapshots..."
echo ""

# Take before snapshots
for service in "${services[@]}"; do
    before_sizes[$service]=$(get_size "$service")
    printf "%-25s %s\n" "$service:" "$(format_size ${before_sizes[$service]})"
done

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Function to safely remove a file or directory
safe_remove() {
    local path=$1
    local description=$2
    if [ -e "$path" ]; then
        echo "  ✓ Removing $description"
        rm -rf "$path"
    fi
}

# Bun service
echo "📦 Cleaning Bun service..."
cd implementations/bun
safe_remove "bunReferenceService" "Bun binary"
safe_remove "node_modules" "node_modules directory"
safe_remove "generated" "Prisma generated code directory"
cd ../..
echo ""

# Dotnet9 service
echo "🔷 Cleaning .NET 9 service..."
cd implementations/dotnet9
safe_remove "bin" "bin directory"
safe_remove "obj" "obj directory"
cd ../..
echo ""

# Dotnet9.events service
echo "🔷 Cleaning .NET 9 Events service..."
cd implementations/dotnet9.events
safe_remove "bin" "bin directory"
safe_remove "obj" "obj directory"
cd ../..
echo ""

# Dotnet9aot service
echo "🔷 Cleaning .NET 9 AOT service..."
cd implementations/dotnet9aot
safe_remove "bin" "bin directory"
safe_remove "obj" "obj directory"
safe_remove "build.log" "build log file"
cd ../..
echo ""

# Go service
echo "🐹 Cleaning Go service..."
cd implementations/go
safe_remove "go-service" "go-service binary"
safe_remove "goReferenceService" "goReferenceService binary"
safe_remove "performancetest-service" "performancetest-service binary"
cd ../..
echo ""

# Java21.events service
echo "☕ Cleaning Java 21 Events service..."
cd implementations/java21.events
safe_remove "target" "target directory"
cd ../..
echo ""

# Java21quarkusgraal service
echo "☕ Cleaning Java 21 Quarkus GraalVM service..."
cd implementations/java21quarkusgraal
safe_remove "target" "target directory"
cd ../..
echo ""

# Java21springboot service
echo "☕ Cleaning Java 21 Spring Boot service..."
cd implementations/java21springboot
safe_remove "target" "target directory"
safe_remove "service.log" "service log file"
cd ../..
echo ""

# Java21springbootgraal service
echo "☕ Cleaning Java 21 Spring Boot GraalVM service..."
cd implementations/java21springbootgraal
safe_remove "target" "target directory"
cd ../..
echo ""

# Java25.events service
echo "☕ Cleaning Java 25 Events service..."
cd implementations/java25.events
safe_remove "target" "target directory"
cd ../..
echo ""

# Java25springboot service
echo "☕ Cleaning Java 25 Spring Boot service..."
cd implementations/java25springboot
safe_remove "target" "target directory"
safe_remove "service.log" "service log file"
cd ../..
echo ""

# Java25springbootgraal service
echo "☕ Cleaning Java 25 Spring Boot GraalVM service..."
cd implementations/java25springbootgraal
safe_remove "target" "target directory"
cd ../..
echo ""

# Java25quarkusgraal service
echo "☕ Cleaning Java 25 Quarkus GraalVM service (Experimental)..."
cd implementations/java25quarkusgraal
safe_remove "target" "target directory"
cd ../..
echo ""


# Python service
echo "🐍 Cleaning Python service..."
cd implementations/python
safe_remove "__pycache__" "__pycache__ directory"
find . -type f -name "*.pyc" -delete 2>/dev/null || true
find . -type d -name "__pycache__" -delete 2>/dev/null || true
cd ../..
echo ""

# Rust service
echo "🦀 Cleaning Rust service..."
cd implementations/rust
safe_remove "target" "target directory"
safe_remove "rustReferenceService" "rustReferenceService binary"
safe_remove "rust-actix-reference-service" "rust-actix-reference-service binary"
cd ../..
echo ""

# Global cleanup
echo "🧼 Cleaning global artifacts..."
# Clean all __pycache__ directories across the project
find . -type d -name "__pycache__" -exec rm -rf {} + 2>/dev/null || true
# Clean all .pyc files across the project
find . -type f -name "*.pyc" -delete 2>/dev/null || true
# Clean all log files across the project
find . -type f -name "*.log" -delete 2>/dev/null || true
echo "  ✓ Removed Python cache directories and compiled files"
echo "  ✓ Removed log files"
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "📊 Taking final size snapshots..."
echo ""

# Take after snapshots and calculate savings
total_saved=0
for service in "${services[@]}"; do
    after_sizes[$service]=$(get_size "$service")
    saved=$((${before_sizes[$service]} - ${after_sizes[$service]}))
    total_saved=$((total_saved + saved))

    printf "%-25s Before: %-12s After: %-12s Saved: %s\n" \
        "$service:" \
        "$(format_size ${before_sizes[$service]})" \
        "$(format_size ${after_sizes[$service]})" \
        "$(format_size $saved)"
done

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
printf "✅ Total space saved: %s\n" "$(format_size $total_saved)"
echo ""
echo "To rebuild, run the appropriate build command for each service:"
echo "  - Bun: bunx prisma generate && bun install && bun build index.ts --compile --outfile bunReferenceService"
echo "  - .NET: dotnet build or dotnet publish"
echo "  - Go: go build"
echo "  - Java: mvn package"
echo "  - Python: No build needed (interpreted)"
echo "  - Rust: cargo build --release"
