-- Create all databases for Performance Test 2025
-- Each service uses its own dedicated database for isolation
-- Idempotent: Silently ignores "already exists" errors (safe to run repeatedly)

-- Note: PostgreSQL doesn't support "CREATE DATABASE IF NOT EXISTS" syntax
-- The ensure_databases() function in run-all-tests.sh filters out "already exists" errors

SELECT 'CREATE DATABASE dotnet9_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'dotnet9_db')\gexec

SELECT 'CREATE DATABASE dotnet9aot_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'dotnet9aot_db')\gexec

SELECT 'CREATE DATABASE python_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'python_db')\gexec

SELECT 'CREATE DATABASE rust_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'rust_db')\gexec

SELECT 'CREATE DATABASE go_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'go_db')\gexec

SELECT 'CREATE DATABASE bun_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'bun_db')\gexec

SELECT 'CREATE DATABASE java21springboot_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'java21springboot_db')\gexec

SELECT 'CREATE DATABASE java21springbootgraal_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'java21springbootgraal_db')\gexec

SELECT 'CREATE DATABASE java21quarkus_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'java21quarkus_db')\gexec

SELECT 'CREATE DATABASE java25springboot_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'java25springboot_db')\gexec

SELECT 'CREATE DATABASE java25springbootgraal_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'java25springbootgraal_db')\gexec

SELECT 'CREATE DATABASE java25quarkus_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'java25quarkus_db')\gexec
