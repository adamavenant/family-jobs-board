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

generate_validation_value() {
  od -An -N32 -tx1 /dev/urandom | tr -d ' \n'
}

validation_pin_pepper=$(generate_validation_value)
validation_jwt_signing_key=$(generate_validation_value)
case "$validation_pin_pepper$validation_jwt_signing_key" in
  '' | *[!0-9a-f]*) fail "could not generate isolated validation credentials" ;;
esac
[ "${#validation_pin_pepper}" -eq 64 ] || fail "could not generate isolated validation credentials"
[ "${#validation_jwt_signing_key}" -eq 64 ] || fail "could not generate isolated validation credentials"
[ "$validation_pin_pepper" != "$validation_jwt_signing_key" ] || fail "validation credentials must be independent"

# These fresh values exist only for this Compose model check. They are never
# printed, persisted, or used by a running application.
auth_environment="AUTHENTICATION_PIN_PEPPER=$validation_pin_pepper AUTHENTICATION_JWT_SIGNING_KEY=$validation_jwt_signing_key APPLICATION_ORIGIN=http://dashboard.home.arpa"

if env $auth_environment DATABASE_PASSWORD= IMAGE_TAG=deployment-image-tag docker compose -f compose.yaml -f compose.production.yaml config >/dev/null 2>&1; then
  fail "production configuration accepted an empty database password"
fi

if env $auth_environment DATABASE_PASSWORD=deployment-config-check IMAGE_TAG= docker compose -f compose.yaml -f compose.production.yaml config >/dev/null 2>&1; then
  fail "production configuration accepted an empty image tag"
fi

if DATABASE_PASSWORD=deployment-config-check IMAGE_TAG=deployment-image-tag AUTHENTICATION_PIN_PEPPER= AUTHENTICATION_JWT_SIGNING_KEY= APPLICATION_ORIGIN= docker compose -f compose.yaml -f compose.production.yaml config >/dev/null 2>&1; then
  fail "production configuration accepted empty authentication secrets"
fi

production_config=$(env $auth_environment DATABASE_PASSWORD=deployment-config-check IMAGE_TAG=deployment-image-tag docker compose -f compose.yaml -f compose.production.yaml config)
production_migrate=$(printf '%s\n' "$production_config" | service_block migrate)
production_api=$(printf '%s\n' "$production_config" | service_block api)
production_db=$(printf '%s\n' "$production_config" | service_block db)
production_web=$(printf '%s\n' "$production_config" | service_block web)
production_proxy=$(printf '%s\n' "$production_config" | service_block proxy)

printf '%s\n' "$production_migrate" | grep -q 'image: ghcr.io/adamavenant/family-jobs-board-migrate:deployment-image-tag' || fail "production migration image is not selected by IMAGE_TAG"
printf '%s\n' "$production_api" | grep -q 'image: ghcr.io/adamavenant/family-jobs-board-api:deployment-image-tag' || fail "production API image is not selected by IMAGE_TAG"
printf '%s\n' "$production_web" | grep -q 'image: ghcr.io/adamavenant/family-jobs-board-web:deployment-image-tag' || fail "production web image is not selected by IMAGE_TAG"
printf '%s\n' "$production_migrate$production_api$production_web" | grep -q '^    build:' && fail "production application images include build instructions"
printf '%s\n' "$production_api" | grep -q '^    ports:' && fail "production API is published"
printf '%s\n' "$production_db" | grep -q '^    ports:' && fail "production database is published"
printf '%s\n' "$production_web" | grep -q '^    ports:' && fail "production web container is published directly"
printf '%s\n' "$production_proxy" | grep -q 'image: caddy:2.11.4-alpine' || fail "production proxy does not use the expected Caddy image"
printf '%s\n' "$production_proxy" | grep -q 'APP_HOSTNAME: dashboard.home.arpa' || fail "production proxy does not default to dashboard.home.arpa"
printf '%s\n' "$production_proxy" | grep -q 'published: "80"' || fail "production proxy does not publish the website on port 80"
printf '%s\n' "$production_proxy" | grep -q 'target: /etc/caddy/Caddyfile' || fail "production proxy does not mount its Caddyfile"
printf '%s\n' "$production_api" | grep -q 'Authentication__AllowedOrigin: http://dashboard.home.arpa' || fail "production API origin is not explicit"

echo "Compose configuration checks passed."
