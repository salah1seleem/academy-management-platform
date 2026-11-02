#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "$0")/../.."
backup_dir=${1:?Usage: backup.sh /absolute/private/backup-directory}
[[ "$backup_dir" = /* && ! -e "$backup_dir" ]] || { echo 'Use a new absolute backup directory.' >&2; exit 1; }
mkdir -p "$backup_dir"
# Freeze all writers, including import/media workers hosted inside API.
running=$(docker compose ps --status running --services)
grep -qx api <<< "$running" && grep -qx web <<< "$running"
resume() { docker compose start api web >/dev/null; }
trap resume EXIT
docker compose stop web api >/dev/null
docker compose exec -T db sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$backup_dir/database.dump"
docker compose run --rm --no-deps -T --user root --entrypoint tar api -C /data -czf - media keys > "$backup_dir/files.tar.gz"
db_container=$(docker compose ps -q db)
docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' "$db_container" > "$backup_dir/source-project.txt"
docker inspect --format '{{.Config.Image}} {{.Image}}' $(docker compose ps -aq api web) > "$backup_dir/image-tag.txt"
(cd "$backup_dir" && sha256sum database.dump files.tar.gz source-project.txt image-tag.txt > SHA256SUMS)
echo 'Backup complete. Keep it encrypted outside the VPS; it includes private media and session keys.'
