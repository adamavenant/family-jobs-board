#!/bin/sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repository_root"

fail() {
  echo "Compose configuration check failed: $1" >&2
  exit 1
}

service_block() {
  service_name=$1
  awk -v service_name="$service_name" '
    $0 == "  " service_name ":" { in_service = 1; next }
    in_service && /^  [A-Za-z0-9_-]+:$/ { exit }
    in_service { print }
  '
}

dev_config=$(docker compose config)
dev_api=$(printf '%s\n' "$dev_config" | service_block api)
dev_web=$(printf '%s\n' "$dev_config" | service_block web)

printf '%s\n' "$dev_api" | grep -q 'published: "8080"' || fail "development API port 8080 is not published"
printf '%s\n' "$dev_web" | grep -q 'published: "3000"' || fail "development web port 3000 is not published"

if DATABASE_PASSWORD= docker compose -f compose.yaml -f compose.production.yaml config >/dev/null 2>&1; then
  fail "production configuration accepted an empty database password"
fi

production_config=$(DATABASE_PASSWORD=deployment-config-check docker compose -f compose.yaml -f compose.production.yaml config)
production_api=$(printf '%s\n' "$production_config" | service_block api)
production_db=$(printf '%s\n' "$production_config" | service_block db)
production_web=$(printf '%s\n' "$production_config" | service_block web)

printf '%s\n' "$production_api" | grep -q '^    ports:' && fail "production API is published"
printf '%s\n' "$production_db" | grep -q '^    ports:' && fail "production database is published"
printf '%s\n' "$production_web" | grep -q 'published: "80"' || fail "production web port does not default to 80"

echo "Compose configuration checks passed."
