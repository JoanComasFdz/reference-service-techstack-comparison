#!/bin/bash

# Clean Service Data Script
# Truncates all tables in a specified database for a clean test run
# This script dynamically discovers table names, so it works for any service

set -euo pipefail

# Check arguments
if [ $# -eq 0 ]; then
    echo "ERROR: Database name required"
    echo "Usage: $0 <database_name>"
    echo ""
    echo "Example: $0 go_db"
    exit 1
fi

DB_NAME="$1"
MAX_RETRIES=2
RETRY_DELAY=2

echo "========================================="
echo "Cleaning Service Data"
echo "========================================="
echo "Database: $DB_NAME"
echo ""

# Check if PostgreSQL container is running
if ! docker ps | grep -q "performancetest-postgres"; then
    echo "ERROR: PostgreSQL container is not running"
    echo "Start it with: docker-compose up -d"
    exit 1
fi

# Get PostgreSQL container name
POSTGRES_CONTAINER="performancetest-postgres"

# Function to truncate tables (with retry logic)
truncate_tables() {
    local attempt=$1

    echo "Attempt $attempt of $MAX_RETRIES:"
    echo ""

    # Query PostgreSQL schema to get all table names
    echo "Discovering tables in database: $DB_NAME"
    TABLES=$(docker exec "$POSTGRES_CONTAINER" psql -U admin -d "$DB_NAME" -t -c \
        "SELECT tablename FROM pg_tables WHERE schemaname='public';" 2>&1)

    # Check if database exists
    if echo "$TABLES" | grep -q "FATAL: database \"$DB_NAME\" does not exist"; then
        echo "ERROR: Database '$DB_NAME' does not exist"
        echo ""
        echo "Available databases:"
        docker exec "$POSTGRES_CONTAINER" psql -U admin -d postgres -t -c \
            "SELECT datname FROM pg_database WHERE datname NOT IN ('postgres', 'template0', 'template1');"
        return 1
    fi

    # Check if command failed
    if [ $? -ne 0 ]; then
        echo "ERROR: Failed to query database schema"
        echo "Output: $TABLES"
        return 1
    fi

    # Remove leading/trailing whitespace and check if empty
    TABLES=$(echo "$TABLES" | tr -d ' ' | grep -v '^$' || true)

    if [ -z "$TABLES" ]; then
        echo "No tables found in $DB_NAME"
        echo "This is normal for first run (services will create tables on startup)"
        echo ""
        return 0
    fi

    echo "Found tables:"
    echo "$TABLES" | sed 's/^/  - /'
    echo ""

    # Truncate each table
    local failed_tables=()
    for table in $TABLES; do
        echo "Truncating table: $table"
        if ! docker exec "$POSTGRES_CONTAINER" psql -U admin -d "$DB_NAME" -c \
            "TRUNCATE TABLE \"$table\" CASCADE;" >/dev/null 2>&1; then
            echo "  ✗ Failed to truncate $table"
            failed_tables+=("$table")
        else
            echo "  ✓ Truncated $table"
        fi
    done

    # Check if any truncations failed
    if [ ${#failed_tables[@]} -gt 0 ]; then
        echo ""
        echo "WARNING: Failed to truncate ${#failed_tables[@]} table(s):"
        printf '  - %s\n' "${failed_tables[@]}"
        return 1
    fi

    return 0
}

# Function to verify tables are empty
verify_cleanup() {
    echo ""
    echo "Verifying cleanup..."

    # Get total row count across all tables
    TOTAL_ROWS=$(docker exec "$POSTGRES_CONTAINER" psql -U admin -d "$DB_NAME" -t -c \
        "SELECT COALESCE(SUM(n_live_tup), 0) FROM pg_stat_user_tables;" 2>/dev/null | tr -d ' ')

    if [ -z "$TOTAL_ROWS" ] || [ "$TOTAL_ROWS" = "0" ]; then
        echo "✓ Verification passed: All tables are empty"
        return 0
    else
        echo "✗ Verification failed: Found $TOTAL_ROWS row(s) in database"
        return 1
    fi
}

# Main execution with retry logic
success=false
for attempt in $(seq 1 $MAX_RETRIES); do
    if truncate_tables "$attempt"; then
        if verify_cleanup; then
            success=true
            break
        fi
    fi

    if [ $attempt -lt $MAX_RETRIES ]; then
        echo ""
        echo "Retrying in ${RETRY_DELAY}s..."
        sleep $RETRY_DELAY
        echo ""
    fi
done

echo ""
echo "========================================="
if [ "$success" = true ]; then
    echo "Database cleaned successfully"
    echo "========================================="
    exit 0
else
    echo "Database cleanup FAILED after $MAX_RETRIES attempts"
    echo "========================================="
    exit 1
fi
