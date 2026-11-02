-- Apply only to an explicitly authorized, dedicated application database.
-- Revokes PUBLIC CREATE/TEMP and schema/object rights; review other users first.
-- First apply reviewed migrations as ddl_role, without starting the API.
-- Invoke psql -X -v expected_database=iacademy -v ddl_role=doadmin -v runtime_role=iacademy_app -f infra/app-platform/runtime-grants.sql
\set ON_ERROR_STOP on
SELECT current_database() = :'expected_database'
   AND current_user = :'ddl_role'
   AND current_user <> :'runtime_role' AS correct_target \gset
\if :correct_target
\else
  \echo 'Wrong database or executor; no grants applied.'
  DO $$ BEGIN RAISE EXCEPTION 'Wrong target; grants not applied'; END $$;
\endif

SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=:'runtime_role'
   AND NOT rolsuper AND NOT rolcreaterole AND NOT rolcreatedb
   AND NOT rolreplication AND NOT rolbypassrls)
   AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datname=current_database()
       AND pg_has_role(:'runtime_role',datdba,'USAGE'))
   AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname<>:'runtime_role'
       AND (pg_has_role(:'runtime_role',oid,'MEMBER')
         OR pg_has_role(:'runtime_role',oid,'SET')))
   AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname='public'
       AND pg_has_role(:'runtime_role',nspowner,'USAGE'))
   AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
       WHERE n.nspname='public' AND pg_has_role(:'runtime_role',c.relowner,'USAGE'))
   AS safe_role \gset
\if :safe_role
\else
  \echo 'Runtime ownership or elevated/inherited rights need review BEFORE grants.'
  DO $$ BEGIN RAISE EXCEPTION 'Unsafe runtime role; grants not applied'; END $$;
\endif
SELECT to_regclass('public.app_data_protection_keys') IS NOT NULL
   AND to_regclass('public."__EFMigrationsHistory"') IS NOT NULL AS migrated \gset
\if :migrated
\else
  \echo 'Migrations must finish before grants; API must remain stopped.'
  DO $$ BEGIN RAISE EXCEPTION 'Migrations missing; grants not applied'; END $$;
\endif

BEGIN;
REVOKE CREATE, TEMPORARY ON DATABASE :"expected_database" FROM PUBLIC;
REVOKE ALL ON DATABASE :"expected_database" FROM :"runtime_role";
GRANT CONNECT ON DATABASE :"expected_database" TO :"runtime_role";
REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM :"runtime_role";
GRANT USAGE ON SCHEMA public TO :"runtime_role";

REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM :"runtime_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"runtime_role";
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM :"runtime_role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"runtime_role";

ALTER DEFAULT PRIVILEGES FOR ROLE :"ddl_role" IN SCHEMA public REVOKE ALL ON TABLES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE :"ddl_role" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"ddl_role" IN SCHEMA public REVOKE ALL ON SEQUENCES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE :"ddl_role" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO :"runtime_role";

-- Overrides must come AFTER broad grants. Runtime neither owns nor can SET the owner.
REVOKE ALL ON app_data_protection_keys FROM :"runtime_role";
GRANT SELECT, INSERT ON app_data_protection_keys TO :"runtime_role";
REVOKE ALL ON "__EFMigrationsHistory" FROM :"runtime_role";
GRANT SELECT ON "__EFMigrationsHistory" TO :"runtime_role";
ALTER ROLE :"runtime_role" CONNECTION LIMIT 16;

SELECT NOT has_database_privilege(:'runtime_role',current_database(),'CREATE')
   AND NOT has_database_privilege(:'runtime_role',current_database(),'TEMP')
   AND NOT has_schema_privilege(:'runtime_role','public','CREATE')
   AND has_table_privilege(:'runtime_role','app_data_protection_keys','SELECT')
   AND has_table_privilege(:'runtime_role','app_data_protection_keys','INSERT')
   AND NOT has_table_privilege(:'runtime_role','app_data_protection_keys','UPDATE')
   AND NOT has_table_privilege(:'runtime_role','app_data_protection_keys','DELETE')
   AND NOT has_table_privilege(:'runtime_role','app_data_protection_keys','TRUNCATE')
   AND NOT has_table_privilege(:'runtime_role','"__EFMigrationsHistory"','INSERT')
   AND NOT has_table_privilege(:'runtime_role','"__EFMigrationsHistory"','DELETE')
   AS least_privilege \gset
\if :least_privilege
  COMMIT;
\else
  ROLLBACK;
  \echo 'Effective privileges still too broad; rolled back.'
  DO $$ BEGIN RAISE EXCEPTION 'Privileges not restricted; rolled back'; END $$;
\endif
