#!/bin/sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repository_root"

fail() {
  echo "Observability configuration check failed: $1" >&2
  exit 1
}

command -v docker >/dev/null 2>&1 || fail "docker is required"
command -v jq >/dev/null 2>&1 || fail "jq is required"

validation_dir=$(mktemp -d "${TMPDIR:-/tmp}/fjb-observability-config.XXXXXX")
trap 'rm -rf "$validation_dir"' EXIT HUP INT TERM

umask 077
printf '%s\n' 'configuration-validation-password-not-for-deployment' > "$validation_dir/grafana-admin-password"
mkdir -p "$validation_dir/prometheus" "$validation_dir/loki"

export GRAFANA_ADMIN_PASSWORD_FILE="$validation_dir/grafana-admin-password"
export PROMETHEUS_DATA_DIR="$validation_dir/prometheus"
export LOKI_DATA_DIR="$validation_dir/loki"

docker compose \
  -f observability/compose.yaml \
  config --format json > "$validation_dir/base.json"

docker compose \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  config --format json > "$validation_dir/storage.json"

GRAFANA_TEST_PORT=33000 docker compose \
  -f observability/compose.yaml \
  -f observability/compose.test-ingress.yaml \
  config --format json > "$validation_dir/test-ingress.json"

assert_jq() {
  file=$1
  expression=$2
  message=$3
  jq -e "$expression" "$file" >/dev/null || fail "$message"
}

assert_jq "$validation_dir/base.json" \
  '.name == "family-jobs-board-observability"' \
  "base project name is not distinct and stable"
assert_jq "$validation_dir/base.json" \
  '[.services[] | (.ports // []) | length] | all(. == 0)' \
  "base configuration publishes a host port"
assert_jq "$validation_dir/base.json" \
  '.networks.backend.internal == true and .networks.query.internal == true and .networks.telemetry.external == true' \
  "backend and query networks must be internal and telemetry must be an external preflighted network"
for service_name in alloy blackbox grafana loki prometheus query-gateway; do
  assert_jq "$validation_dir/base.json" \
    ".services[\"$service_name\"].healthcheck.test != null" \
    "$service_name has no health check"
  assert_jq "$validation_dir/base.json" \
    ".services[\"$service_name\"].read_only == true and (.services[\"$service_name\"].cap_drop | index(\"ALL\") != null) and (.services[\"$service_name\"].security_opt | index(\"no-new-privileges:true\") != null)" \
    "$service_name is missing the read-only/no-capabilities security baseline"
done

for service_name in storage-init alloy blackbox grafana loki prometheus query-gateway; do
  assert_jq "$validation_dir/base.json" \
    ".services[\"$service_name\"].logging.driver == \"local\" and .services[\"$service_name\"].logging.options[\"max-size\"] != null and .services[\"$service_name\"].logging.options[\"max-file\"] != null" \
    "$service_name does not have bounded local container logs"
done

assert_jq "$validation_dir/base.json" \
  '.services.grafana.environment.GF_AUTH_ANONYMOUS_ORG_ROLE == "Viewer" and .services.grafana.environment.GF_USERS_VIEWERS_CAN_EDIT == "false" and .services.grafana.environment.GF_USERS_EDITORS_CAN_ADMIN == "false" and .services.grafana.environment.GF_USERS_ALLOW_SIGN_UP == "false"' \
  "Grafana anonymous Viewer restrictions are incomplete"
assert_jq "$validation_dir/base.json" \
  '.services.grafana.environment.GF_SECURITY_ADMIN_PASSWORD == null and .services.grafana.environment.GF_SECURITY_ADMIN_PASSWORD__FILE == "/tmp/grafana-admin-password" and .services.grafana.entrypoint == ["/usr/local/bin/grafana-secure-entrypoint"]' \
  "Grafana operator credential is not supplied through the secure file wrapper"
assert_jq "$validation_dir/base.json" \
  '.services.prometheus.command | index("--query.timeout=30s") != null and index("--query.max-concurrency=4") != null and index("--query.max-samples=5000000") != null' \
  "Prometheus anonymous-query limits are incomplete"
assert_jq "$validation_dir/base.json" \
  '(.services.grafana.networks | keys) == ["query"] and (.services["query-gateway"].networks | keys | sort) == ["backend", "query"] and (.services.prometheus.networks | keys) == ["backend"] and (.services.loki.networks | keys) == ["backend"]' \
  "Grafana is not network-isolated behind the query gateway"
assert_jq "$validation_dir/base.json" \
  '[.services | to_entries[] | .value.image] | all((endswith(":latest") or contains(":latest@")) | not)' \
  "a service uses a floating latest image"
assert_jq "$validation_dir/base.json" \
  '[.services | to_entries[] | .value.image] | all(test(":[^/:]+$"))' \
  "a service image is not version tagged"

if grep -Eq '(/var/run/docker\.sock|/var/lib/docker|/proc([/:]|$)|/sys([/:]|$))' "$validation_dir/base.json"; then
  fail "base configuration mounts a forbidden Docker or host-internals path"
fi

grep -q 'url: http://query-gateway:8080/prometheus' \
  observability/config/grafana/provisioning/datasources/datasources.yaml \
  || fail "Grafana Prometheus data source bypasses the query gateway"
grep -q 'url: http://query-gateway:8080/loki' \
  observability/config/grafana/provisioning/datasources/datasources.yaml \
  || fail "Grafana Loki data source bypasses the query gateway"
if grep -Eq 'url: http://(prometheus:9090|loki:3100)' \
  observability/config/grafana/provisioning/datasources/datasources.yaml; then
  fail "Grafana has a direct Prometheus or Loki data source"
fi
grep -q 'metrics_path: /grafana/metrics' observability/config/prometheus/prometheus.yaml \
  || fail "Prometheus does not scrape Grafana through the query gateway"
grep -q 'http://query-gateway:8080/grafana/api/health' observability/config/prometheus/prometheus.yaml \
  || fail "Blackbox does not probe Grafana through the query gateway"
if grep -Eq '(http://)?grafana:3000' observability/config/prometheus/prometheus.yaml; then
  fail "a backend collector bypasses the Grafana gateway bridge"
fi
preflight_env_load_count=$(grep -Fc \
  "sudo sh -c 'set -eu; set -a; . /etc/family-jobs-board-observability/observability.env; set +a; exec ./scripts/ensure-observability-network.sh'" \
  docs/operations/observability.md || true)
[ "$preflight_env_load_count" = "2" ] \
  || fail "each documented network preflight must load the root-only environment file inside a privileged shell"

assert_jq "$validation_dir/storage.json" \
  '.services["storage-preflight"].environment.PROMETHEUS_VOLUME_BUDGET_MIB == "2048" and .services["storage-preflight"].environment.LOKI_VOLUME_BUDGET_MIB == "4096"' \
  "storage budgets are missing or changed"
assert_jq "$validation_dir/storage.json" \
  '.services["storage-init"].depends_on["storage-preflight"].condition == "service_completed_successfully"' \
  "storage initialization does not wait for the capacity preflight"
assert_jq "$validation_dir/storage.json" \
  '.volumes["prometheus-data"].driver_opts.device != null and .volumes["loki-data"].driver_opts.device != null' \
  "production telemetry volumes are not explicit bind-backed filesystems"

assert_jq "$validation_dir/test-ingress.json" \
  '[.services | to_entries[] | select(((.value.ports // []) | length) > 0) | .key] == ["grafana"]' \
  "test ingress publishes a service other than Grafana"
assert_jq "$validation_dir/test-ingress.json" \
  '.services.grafana.ports | length == 1 and .[0].host_ip == "127.0.0.1" and .[0].target == 3000' \
  "test Grafana ingress is not loopback-only"
assert_jq "$validation_dir/test-ingress.json" \
  '(.services.grafana.networks | keys | sort) == ["query", "test-ingress"] and (.services.grafana.networks.backend == null)' \
  "test Grafana ingress bypasses query-gateway network isolation"

jq -e . observability/config/grafana/dashboards/telemetry-stack-health.json >/dev/null \
  || fail "Grafana dashboard is not valid JSON"
jq -e \
  '.uid == "telemetry-stack-health" and .editable == false and (.panels | length >= 6) and all(.panels[]; .datasource.uid != null)' \
  observability/config/grafana/dashboards/telemetry-stack-health.json >/dev/null \
  || fail "Grafana health dashboard is incomplete or editable"

grep -q 'time: 30d' observability/config/prometheus/prometheus.yaml \
  || fail "Prometheus 30-day retention is missing"
grep -q 'size: 1600MB' observability/config/prometheus/prometheus.yaml \
  || fail "Prometheus size retention is missing"
grep -q 'retention_period: 720h' observability/config/loki/loki.yaml \
  || fail "Loki 720-hour retention is missing"
grep -q 'working_directory: /loki/compactor' observability/config/loki/loki.yaml \
  || fail "Loki Compactor state is not on persistent storage"
grep -Fq "sudo sh -c 'cd -- \"\$1\" && sha256sum -- *.tgz > SHA256SUMS' sh \"\$backup_dir\"" \
  docs/operations/observability.md \
  || fail "backup checksums do not expand inside the privileged root-owned directory"
grep -Fq "sudo sh -c 'cd -- \"\$1\" && sha256sum --check SHA256SUMS' sh \"\$backup_dir\"" \
  docs/operations/observability.md \
  || fail "backup checksum verification does not run inside the privileged root-owned directory"
retired_observability_host='familydash'"."'home'"."'arpa'
if grep -R -n "$retired_observability_host" docs observability scripts; then
  fail "retired observability hostname is still present"
fi

docker run --rm --network none --read-only --cap-drop ALL --tmpfs /tmp:size=16m \
  --entrypoint /bin/promtool \
  -v "$repository_root/observability/config/prometheus/prometheus.yaml:/etc/prometheus/prometheus.yaml:ro" \
  prom/prometheus:v3.15.0 \
  check config /etc/prometheus/prometheus.yaml

docker run --rm --network none --read-only --cap-drop ALL --tmpfs /tmp:size=16m \
  -v "$repository_root/observability/config/loki/loki.yaml:/etc/loki/config.yaml:ro" \
  grafana/loki:3.7.8 \
  -config.file=/etc/loki/config.yaml -verify-config=true

docker run --rm --network none --read-only --cap-drop ALL --tmpfs /tmp:size=16m \
  -v "$repository_root/observability/config/alloy/config.alloy:/etc/alloy/config.alloy:ro" \
  grafana/alloy:v1.20.1 \
  validate /etc/alloy/config.alloy

docker run --rm --network none --read-only --cap-drop ALL --tmpfs /tmp:size=16m \
  -v "$repository_root/observability/config/blackbox/blackbox.yaml:/etc/blackbox_exporter/config.yaml:ro" \
  prom/blackbox-exporter:v0.28.0 \
  --config.file=/etc/blackbox_exporter/config.yaml --config.check

docker run --rm --network none --read-only --cap-drop ALL --user 101:101 \
  --tmpfs /tmp:size=16m --add-host prometheus:127.0.0.1 --add-host loki:127.0.0.1 \
  --entrypoint /usr/sbin/nginx \
  -v "$repository_root/observability/config/query-gateway/nginx.conf:/etc/nginx/nginx.conf:ro" \
  nginx:1.31.5-alpine3.24 \
  -t -c /etc/nginx/nginx.conf

echo "Observability configuration checks passed."
