#!/usr/bin/env bash
set -euo pipefail
# Runs once on a NEW PostgreSQL volume. Runtime cannot own or migrate the schema.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=runtime_password="$API_DATABASE_PASSWORD" --set=database_name="$POSTGRES_DB" <<'SQL'
CREATE ROLE iacademy_app LOGIN PASSWORD :'runtime_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database_name" TO iacademy_app;
GRANT USAGE ON SCHEMA public TO iacademy_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO iacademy_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO iacademy_app;
SQL
