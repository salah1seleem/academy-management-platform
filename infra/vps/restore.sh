#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "$0")/../.."
backup_dir=${1:?Usage: restore.sh /absolute/private/backup-directory}
[[ "$backup_dir" = /* ]] || exit 1
(cd "$backup_dir" && shasum -a 256 -c SHA256SUMS)
source_project=$(cat "$backup_dir/source-project.txt")
[[ -n "${COMPOSE_PROJECT_NAME:-}" && "$COMPOSE_PROJECT_NAME" != "$source_project" ]] || { echo 'Restore requires a different, explicit COMPOSE_PROJECT_NAME.' >&2; exit 1; }
# Never replace an existing deployment or merge with existing files.
[[ -z "$(docker compose ps -aq api web migrate)" ]] || { echo 'Target must be a fresh isolated deployment.' >&2; exit 1; }
docker compose up -d --wait db
count=$(docker compose exec -T db sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('"'"'pg_catalog'"'"', '"'"'information_schema'"'"')"')
[[ "$count" = 0 ]] || { echo 'Target database must be empty.' >&2; exit 1; }
docker compose run --rm --no-deps -T --user root --entrypoint sh api -c 'test -z "$(find /data/media /data/keys -mindepth 1 -print -quit)"'
docker compose exec -T db sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-acl --single-transaction --exit-on-error' < "$backup_dir/database.dump"
docker compose run --rm --no-deps -T --user root --entrypoint tar api -C /data -xzf - < "$backup_dir/files.tar.gz"
echo 'Restored to fresh target. Start services and verify roles, isolation, media and session continuity before any cutover.'
