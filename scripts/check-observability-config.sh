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
export DOCKER_GID=0

docker compose \
  -f observability/compose.yaml \
  config --format json > "$validation_dir/base.json"

docker compose \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  config --format json > "$validation_dir/storage.json"

docker compose \
  -f observability/compose.yaml \
  -f observability/compose.ingress.yaml \
  config --format json > "$validation_dir/ingress.json"

docker compose \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  -f observability/compose.ingress.yaml \
  -f observability/compose.snap-docker-compat.yaml \
  config --format json > "$validation_dir/snap-docker-compat.json"

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
  '.networks.backend.internal == true and .networks.query.internal == true and .networks.probes.internal != true and .networks.telemetry.external == true and .networks["docker-api"] == null' \
  "backend, query, probe, and telemetry networks do not have the required isolation"
for service_name in alloy blackbox container-exporter grafana loki node-exporter prometheus query-gateway; do
  assert_jq "$validation_dir/base.json" \
    ".services[\"$service_name\"].healthcheck.test != null" \
    "$service_name has no health check"
  assert_jq "$validation_dir/base.json" \
    ".services[\"$service_name\"].read_only == true and (.services[\"$service_name\"].cap_drop | index(\"ALL\") != null) and (.services[\"$service_name\"].security_opt | index(\"no-new-privileges:true\") != null)" \
    "$service_name is missing the read-only/no-capabilities security baseline"
done

for service_name in storage-init alloy blackbox container-exporter grafana loki node-exporter prometheus query-gateway; do
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
  '.services.grafana.environment.GF_DASHBOARDS_DEFAULT_HOME_DASHBOARD_PATH == "/var/lib/grafana/dashboards/family-jobs-board-overview.json"' \
  "Family Jobs Board overview is not the default Grafana dashboard"
assert_jq "$validation_dir/base.json" \
  '.services.prometheus.command | index("--query.timeout=30s") != null and index("--query.max-concurrency=4") != null and index("--query.max-samples=5000000") != null' \
  "Prometheus anonymous-query limits are incomplete"
assert_jq "$validation_dir/base.json" \
  '(.services.grafana.networks | keys) == ["query"] and (.services["query-gateway"].networks | keys | sort) == ["backend", "query"] and (.services.prometheus.networks | keys) == ["backend"] and (.services.loki.networks | keys) == ["backend"]' \
  "Grafana is not network-isolated behind the query gateway"
assert_jq "$validation_dir/base.json" \
  '(.services["container-exporter"].networks | keys) == ["backend"] and (.services.blackbox.networks | keys | sort) == ["backend", "probes"] and (.services["node-exporter"].networks | keys) == ["backend"]' \
  "host collectors do not have the required least-privilege network boundaries"
assert_jq "$validation_dir/base.json" \
  '.services["node-exporter"].privileged != true and .services["container-exporter"].privileged != true' \
  "a host collector is privileged"
assert_jq "$validation_dir/base.json" \
  '[.services | to_entries[] | select(any(.value.volumes[]?; .source == "/var/run/docker.sock")) | .key] == ["container-exporter"]' \
  "the Docker socket is mounted outside the privacy-bounded container exporter"
assert_jq "$validation_dir/base.json" \
  '(.services["container-exporter"].volumes[] | select(.source == "/var/run/docker.sock") | .read_only) == true and .services["container-exporter"].environment.DOCKER_API_URL == "unix:///var/run/docker.sock"' \
  "the Docker socket bind mount is not read-only"
assert_jq "$validation_dir/base.json" \
  '(.services["node-exporter"].command | index("--collector.disable-defaults") != null) and (.services["node-exporter"].command | index("--collector.cpu") != null) and (.services["node-exporter"].command | index("--collector.filesystem") != null) and (.services["node-exporter"].command | index("--collector.loadavg") != null) and (.services["node-exporter"].command | index("--collector.meminfo") != null) and (.services["node-exporter"].command | index("--collector.stat") != null)' \
  "node-exporter does not use the approved collector allowlist"
assert_jq "$validation_dir/base.json" \
  '[.services["node-exporter"].command[] | select(startswith("--collector.")) | select(startswith("--collector.filesystem.") | not)] | sort == ["--collector.cpu", "--collector.disable-defaults", "--collector.filesystem", "--collector.loadavg", "--collector.meminfo", "--collector.stat"]' \
  "node-exporter enables a collector outside the exact approved allowlist"
assert_jq "$validation_dir/base.json" \
  '[.services | to_entries[] | .value.image] | all((endswith(":latest") or contains(":latest@")) | not)' \
  "a service uses a floating latest image"
assert_jq "$validation_dir/base.json" \
  '[.services | to_entries[] | .value.image] | all(test(":[^/:]+$"))' \
  "a service image is not version tagged"

if grep -q '/var/lib/docker' "$validation_dir/base.json"; then
  fail "base configuration mounts Docker's data directory"
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
  || fail "each documented network preflight must load the environment file inside a privileged shell"
grep -Fq 'sudo install -o root -g docker -m 750 -d /etc/family-jobs-board-observability' \
  docs/operations/observability.md \
  || fail "the observability environment directory is not readable by Docker operators"
grep -Fq 'sudo install -o root -g docker -m 640 observability/environment.example' \
  docs/operations/observability.md \
  || fail "the observability environment file is not readable by Docker operators"
grep -Fq 'server prometheus:9090 resolve;' observability/config/query-gateway/nginx.conf \
  || fail "the query gateway does not dynamically resolve Prometheus after container replacement"
grep -Fq 'server loki:3100 resolve;' observability/config/query-gateway/nginx.conf \
  || fail "the query gateway does not dynamically resolve Loki after container replacement"
if grep -Fq -- '    - service.version' observability/config/prometheus/prometheus.yaml; then
  fail "Prometheus promotes a resource attribute that Alloy discards"
fi

assert_jq "$validation_dir/storage.json" \
  '.services["storage-preflight"].environment.PROMETHEUS_VOLUME_BUDGET_MIB == "2048" and .services["storage-preflight"].environment.LOKI_VOLUME_BUDGET_MIB == "4096"' \
  "storage budgets are missing or changed"
assert_jq "$validation_dir/storage.json" \
  '.services["storage-init"].depends_on["storage-preflight"].condition == "service_completed_successfully"' \
  "storage initialization does not wait for the capacity preflight"
assert_jq "$validation_dir/storage.json" \
  '.volumes["prometheus-data"].driver_opts.device != null and .volumes["loki-data"].driver_opts.device != null' \
  "production telemetry volumes are not explicit bind-backed filesystems"

assert_jq "$validation_dir/ingress.json" \
  '[.services[] | (.ports // []) | length] | all(. == 0)' \
  "production observability ingress publishes a host port"
assert_jq "$validation_dir/ingress.json" \
  '(.services.grafana.networks | keys | sort) == ["monitor-ingress", "query"] and .services.grafana.networks["monitor-ingress"].aliases == ["observability-grafana"] and .networks["monitor-ingress"].external == true' \
  "production Grafana ingress is not limited to the dedicated external network"
assert_jq "$validation_dir/ingress.json" \
  '([.services | to_entries[] | select(.key != "grafana") | select(.value.networks["monitor-ingress"] != null)] | length) == 0' \
  "an observability backend other than Grafana joins the monitor ingress network"

for service_name in alloy blackbox container-exporter grafana loki node-exporter prometheus query-gateway storage-init storage-preflight; do
  assert_jq "$validation_dir/snap-docker-compat.json" \
    ".services[\"$service_name\"].read_only == true and (.services[\"$service_name\"].cap_drop | index(\"ALL\") != null) and ((.services[\"$service_name\"].security_opt // []) | length) == 0" \
    "$service_name does not retain the non-NNP hardening in the Docker Snap compatibility model"
done
assert_jq "$validation_dir/snap-docker-compat.json" \
  '[.services[] | (.ports // []) | length] | all(. == 0)' \
  "Docker Snap compatibility configuration publishes a host port"
assert_jq "$validation_dir/snap-docker-compat.json" \
  '(.services.grafana.networks | keys | sort) == ["monitor-ingress", "query"] and .services.grafana.networks["monitor-ingress"].aliases == ["observability-grafana"]' \
  "Docker Snap compatibility configuration changes the production ingress boundary"

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
jq -e . observability/config/grafana/dashboards/server-and-service-health.json >/dev/null \
  || fail "Grafana server and service health dashboard is not valid JSON"
jq -e \
  '.uid == "server-and-service-health"
   and .title == "Server and service health"
   and .editable == false
   and (.panels | length >= 18)
   and all(.panels[]; .datasource.uid == "prometheus")
   and all(.panels[]; .fieldConfig.defaults.noValue == "NO DATA")
   and (.timepicker.time_options | index("30d") != null)
   and ([.panels[].targets[].expr] | any(startswith("family_jobs_board_container_present")))
   and ([.panels[].targets[].expr] | any(startswith("family_jobs_board_container_state")))
   and ([.panels[].targets[].expr] | any(startswith("family_jobs_board_container_collector_success")))
   and ([.panels[].targets[].expr] | any(contains("family_jobs_board_telemetry_storage_used_bytes{store=\"prometheus\"}")))
   and ([.panels[].targets[].expr] | any(contains("family_jobs_board_telemetry_storage_used_bytes{store=\"loki\"}")))
   and ([.panels[] | select(.id == 2 or .id == 3 or .id == 4 or .id == 5 or .id == 16) | .targets[0]]
     | all(.instant == true and .range == false and (.expr | contains("timestamp(node_boot_time_seconds"))))
   and ([.panels[] | select(.title == "Container Docker health") | .fieldConfig.defaults.thresholds.steps[-1]] == [{"color":"green","value":1}])
   and ([.panels[] | select(.title == "Deployed images") | .targets[].legendFormat] | all(contains("{{image_id}}")))' \
  observability/config/grafana/dashboards/server-and-service-health.json >/dev/null \
  || fail "Grafana server and service health dashboard is incomplete or editable"
if jq -r '.. | .expr? // empty' \
  observability/config/grafana/dashboards/server-and-service-health.json \
  | grep -Eq '(container_(name|id|environment|mount|command|label)|docker_container_(name|id|environment|mount|command|label))'; then
  fail "server dashboard requests forbidden container inventory fields"
fi

jq -e . observability/config/grafana/dashboards/family-jobs-board-overview.json >/dev/null \
  || fail "Grafana Family Jobs Board overview is not valid JSON"
jq -e \
  '.uid == "family-jobs-board-overview"
   and .title == "Family Jobs Board overview"
   and .editable == false
   and (.panels | length >= 20)
   and all(.panels[]; .datasource.uid == "prometheus" or .datasource.uid == "loki")
   and all(.panels[]; .fieldConfig.defaults.noValue == "NO DATA")
   and all(.panels[]; .type == "stat" or .type == "timeseries" or .type == "state-timeline")
   and (.time.from == "now-6h")
   and (.timepicker.time_options | index("30d") != null)
   and ([.panels[].targets[].expr] | any(contains("family_jobs_board_application_runtime_error_count_total")))
   and ([.panels[].targets[].expr] | any(contains("family_jobs_board_application_warning_count_total")))
   and ([.panels[].targets[].expr] | any(contains("family_jobs_board_authentication_rejection_count_total")))
   and ([.panels[].targets[].expr] | any(contains("aspnetcore_diagnostics_exceptions_total")))
   and ([.panels[].targets[].expr] | any(contains("histogram_quantile(0.50")))
   and ([.panels[].targets[].expr] | any(contains("histogram_quantile(0.95")))
   and ([.panels[] | select(.id == 3 or .id == 4 or .id == 5 or .id == 6 or .id == 10 or .id == 20) | .targets[0].expr]
     | all((contains("increase(") | not)
       and contains("timestamp(")
       and contains(">= scalar(max(family_jobs_board_process_start_time_seconds")))
   and ([.panels[] | select(.id == 1) | .targets[0].expr]
     | all(startswith("(vector(0)")
       and contains("min(up{")
       and contains("container_collector_last_success_timestamp_seconds")
       and contains("timestamp(family_jobs_board_process_start_time_seconds")
       and contains("or (vector(1) and on() ((family_jobs_board_telemetry_storage_collector_success == 0)")))
   and ([.panels[] | select(.id == 17) | .targets[] | select(.refId == "D") | .expr]
     | all(contains("container_collector_last_success_timestamp_seconds") and contains("> bool 120")))
   and ([.links[].url] | all(startswith("/d/")))' \
  observability/config/grafana/dashboards/family-jobs-board-overview.json >/dev/null \
  || fail "Grafana Family Jobs Board overview is incomplete, editable, or unsafe"
if jq -r '.. | .expr? // empty, .url? // empty' \
  observability/config/grafana/dashboards/family-jobs-board-overview.json \
  | grep -Eqi '(container_(name|id|environment|mount|command|label)|docker_container_(name|id|environment|mount|command|label)|member(_|\.)?(id|name)|household(_|\.)?id|request(_|\.)?id|session(_|\.)?id|trace(_|\.)?id|span(_|\.)?id|https?://)'; then
  fail "Family Jobs Board overview requests a forbidden or externally linked field"
fi

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
backup_cap_count=$(grep -Fc -- '--cap-add DAC_READ_SEARCH' docs/operations/observability.md || true)
[ "$backup_cap_count" = "2" ] \
  || fail "Loki and Alloy backup commands do not have the read capability needed for service-owned data"
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

python3 -m unittest discover -v -s observability/container-exporter -p 'test_*.py'

echo "Observability configuration checks passed."
