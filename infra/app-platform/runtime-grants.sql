-- Run as schema owner in a NEW database, after PRE_DEPLOY migrations.
-- Create iacademy_app with a strong password in the provider console, never in Git.
GRANT CONNECT ON DATABASE iacademy TO iacademy_app;
GRANT USAGE ON SCHEMA public TO iacademy_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO iacademy_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO iacademy_app;
-- Execute these AS the migration owner so future tables inherit grants.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO iacademy_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO iacademy_app;
-- Key ring is append-only for runtime.
REVOKE UPDATE, DELETE ON app_data_protection_keys FROM iacademy_app;
