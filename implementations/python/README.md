# Python FastAPI Reference Service

A simple reference implementation using Python 3.13+ and FastAPI.

## Requirements

- Python 3.13+ (minimum 3.9, but 3.13+ recommended for performance)
- RabbitMQ (via docker-compose)
- PostgreSQL (via docker-compose)

**Why Python 3.13+?** The code uses modern type annotations (PEP 585) like `dict[str, Any]` which require Python 3.9+. Python 3.13 provides significant performance improvements (~10-20% faster) and better developer experience.

**Note on Dependencies:** This implementation uses **psycopg3** (`psycopg[binary]>=3.2.0`), not psycopg2. Psycopg3 provides better performance and native Python 3.13 support.

## Installation

**Automated Setup (Recommended):**

The project uses mise for Python version management. Run from the project root:

```bash
# Install all tools including Python 3.13 and dependencies
./setup-environment.sh

# Verify installation
./verify-environment.sh
```

This will install Python 3.13 and all required packages globally via mise.

**Manual Installation (if not using automated setup):**

```bash
# If mise is already installed and activated
python -m pip install -r requirements.txt

# Or create a local virtual environment
python3.13 -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate
pip install -r requirements.txt
```

## Database Setup

The service uses a dedicated database `python_db`. Create it before running the service:

```bash
# Connect to PostgreSQL via Docker
docker exec -it performancetest-postgres psql -U admin -d postgres

# Create the database
CREATE DATABASE python_db;
```

The service will automatically create the `python_instrument_status` table on startup.

## Running the Service

```bash
# Make sure RabbitMQ and PostgreSQL are running
cd ../..
docker-compose up -d

# Run the service using the wrapper script (recommended)
cd implementations/python
./pythonReferenceService

# Alternative: Run directly with mise's Python
python -m uvicorn main:app --host 0.0.0.0 --port 8099

# Or if you have a local venv activated:
python3.13 main.py
```

The service will start on port 8099.

**Note:** The `pythonReferenceService` wrapper script:
- Uses mise to ensure Python 3.13 and all dependencies are available
- Makes the process appear as "pythonReferenceService" in monitoring tools (not "python3")
- Validates that all required packages are installed before starting
- **Recommended for automated testing and production use**

## Testing

Use the provided test.http file in the root directory to test the `/kpi` endpoint.

Or use curl:

```bash
curl http://localhost:8099/kpi
```

## Performance Testing

Run the performance tester from the root directory:

```bash
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8099
```
