#!/bin/sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repository_root"

fail() {
  echo "Observability integration test failed: $1" >&2
  exit 1
}

command -v curl >/dev/null 2>&1 || fail "curl is required"
command -v docker >/dev/null 2>&1 || fail "docker is required"
command -v jq >/dev/null 2>&1 || fail "jq is required"

test_token="$$-$(date +%s)"
test_root=$(mktemp -d "${TMPDIR:-/tmp}/fjb-observability-stack.XXXXXX")
test_project="fjb-observability-it-$test_token"
test_prefix="$test_project"
test_network="$test_project-telemetry"
storage_project="$test_project-storage"
storage_prefix="$test_prefix-storage"
backend_network="$test_prefix-backend"
backup_volume="$test_prefix-backup"

mkdir -p \
  "$test_root/secret" \
  "$test_root/fixtures" \
  "$test_root/oversize-prometheus" \
  "$test_root/oversize-loki"
chmod 0711 "$test_root"
chmod 0700 "$test_root/secret"
chmod 0755 "$test_root/fixtures" "$test_root/oversize-prometheus" "$test_root/oversize-loki"
umask 077
printf '%s\n' "integration-test-password-$test_token" > "$test_root/secret/grafana-admin-password"

export OBSERVABILITY_PROJECT_NAME="$test_project"
export OBSERVABILITY_RESOURCE_PREFIX="$test_prefix"
export TELEMETRY_NETWORK_NAME="$test_network"
export GRAFANA_ADMIN_PASSWORD_FILE="$test_root/secret/grafana-admin-password"
export GRAFANA_TEST_PORT=0
export FJB_COMPOSE_PROJECT_NAME="$test_project-application"

DOCKER_GID=$(docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock:ro \
  busybox:1.37.0-uclibc stat -c '%g' /var/run/docker.sock)
export DOCKER_GID

fixture_containers=""
api_fixture_id=""
deployment_fixture_image=""

compose_test() {
  docker compose \
    -f observability/compose.yaml \
    -f observability/compose.test-ingress.yaml \
    "$@"
}

cleanup() {
  cleanup_status=$?
  trap - EXIT HUP INT TERM

  if [ "$cleanup_status" -ne 0 ]; then
    compose_test ps --all >&2 2>/dev/null || true
    compose_test logs --no-color --tail 100 >&2 2>/dev/null || true
  fi

  compose_test down --volumes --remove-orphans >/dev/null 2>&1 || true
  if [ -n "$fixture_containers" ]; then
    docker rm --force $fixture_containers >/dev/null 2>&1 || true
  fi
  if [ -n "$deployment_fixture_image" ]; then
    docker image rm "$deployment_fixture_image" >/dev/null 2>&1 || true
  fi
  OBSERVABILITY_PROJECT_NAME="$storage_project" \
    OBSERVABILITY_RESOURCE_PREFIX="$storage_prefix" \
    PROMETHEUS_DATA_DIR="$test_root/oversize-prometheus" \
    LOKI_DATA_DIR="$test_root/oversize-loki" \
    docker compose \
      -f observability/compose.yaml \
      -f observability/compose.storage-bounds.yaml \
      down --volumes --remove-orphans >/dev/null 2>&1 || true
  docker network rm "$test_network" >/dev/null 2>&1 || true
  docker volume rm "$backup_volume" >/dev/null 2>&1 || true
  rm -rf "$test_root"
  exit "$cleanup_status"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM

./scripts/ensure-observability-network.sh
network_properties=$(docker network inspect \
  --format '{{.Internal}} {{.Driver}} {{.Scope}}' \
  "$test_network")
[ "$network_properties" = "true bridge local" ] \
  || fail "telemetry network is not a private local bridge ($network_properties)"

for service_name in api db proxy web; do
  fixture_id=$(docker run --detach \
    --label "com.docker.compose.project=$FJB_COMPOSE_PROJECT_NAME" \
    --label "com.docker.compose.service=$service_name" \
    --health-cmd 'true' \
    --health-interval 1s \
    --health-retries 2 \
    busybox:1.37.0-uclibc sleep 600)
  fixture_containers="$fixture_containers $fixture_id"
  if [ "$service_name" = "api" ]; then
    api_fixture_id=$fixture_id
  fi
done
unrelated_fixture_id=$(docker run --detach \
  --label 'com.docker.compose.project=unrelated-test-project' \
  --label 'com.docker.compose.service=api' \
  --health-cmd 'false' \
  busybox:1.37.0-uclibc sleep 600)
fixture_containers="$fixture_containers $unrelated_fixture_id"

# A normal host filesystem is deliberately too large. Production must fail
# closed instead of mistaking these directories for bounded allocations.
if OBSERVABILITY_PROJECT_NAME="$storage_project" \
  OBSERVABILITY_RESOURCE_PREFIX="$storage_prefix" \
  PROMETHEUS_DATA_DIR="$test_root/oversize-prometheus" \
  LOKI_DATA_DIR="$test_root/oversize-loki" \
  docker compose \
    -f observability/compose.yaml \
    -f observability/compose.storage-bounds.yaml \
    up --abort-on-container-exit --exit-code-from storage-preflight storage-preflight \
    >"$test_root/storage-preflight.log" 2>&1; then
  fail "storage preflight accepted an ordinary unbounded host filesystem"
fi
grep -q 'expected a dedicated filesystem no larger than' "$test_root/storage-preflight.log" \
  || fail "storage preflight failed without explaining the enforced capacity budget"

OBSERVABILITY_PROJECT_NAME="$storage_project" \
  OBSERVABILITY_RESOURCE_PREFIX="$storage_prefix" \
  PROMETHEUS_DATA_DIR="$test_root/oversize-prometheus" \
  LOKI_DATA_DIR="$test_root/oversize-loki" \
  docker compose \
    -f observability/compose.yaml \
    -f observability/compose.storage-bounds.yaml \
    down --volumes --remove-orphans >/dev/null

compose_test up --detach --build --wait --wait-timeout 180

for service_name in alloy blackbox container-exporter grafana loki node-exporter prometheus query-gateway; do
  container_id=$(compose_test ps -q "$service_name")
  [ -n "$container_id" ] || fail "$service_name container was not created"
  health=$(docker inspect --format '{{.State.Health.Status}}' "$container_id")
  [ "$health" = "healthy" ] || fail "$service_name is not healthy ($health)"
done

storage_init_id=$(compose_test ps --all -q storage-init)
[ -n "$storage_init_id" ] || fail "storage initializer was not created"
[ "$(docker inspect --format '{{.State.ExitCode}}' "$storage_init_id")" = "0" ] \
  || fail "storage initializer did not complete successfully"

for service_name in alloy blackbox container-exporter loki node-exporter prometheus query-gateway; do
  container_id=$(compose_test ps -q "$service_name")
  port_bindings=$(docker inspect --format '{{json .NetworkSettings.Ports}}' "$container_id")
  printf '%s\n' "$port_bindings" | jq -e 'all(.[]; . == null)' >/dev/null \
    || fail "$service_name has a host-published port"
done

grafana_id=$(compose_test ps -q grafana)
grafana_ports=$(docker inspect --format '{{json .NetworkSettings.Ports}}' "$grafana_id")
printf '%s\n' "$grafana_ports" | jq -e \
  'to_entries | (length == 1) and (.[0].value | length == 1 and .[0].HostIp == "127.0.0.1")' \
  >/dev/null || fail "Grafana verification ingress is not loopback-only"

grafana_networks=$(docker inspect --format '{{json .NetworkSettings.Networks}}' "$grafana_id")
printf '%s\n' "$grafana_networks" | jq -e \
  --arg query "$test_prefix-query" \
  --arg ingress "$test_prefix-test-ingress" \
  'keys | sort == ([$query, $ingress] | sort)' >/dev/null \
  || fail "Grafana is not isolated to the query and loopback-test networks"

query_gateway_id=$(compose_test ps -q query-gateway)
query_gateway_networks=$(docker inspect --format '{{json .NetworkSettings.Networks}}' "$query_gateway_id")
printf '%s\n' "$query_gateway_networks" | jq -e \
  --arg backend "$backend_network" \
  --arg query "$test_prefix-query" \
  'keys | sort == ([$backend, $query] | sort)' >/dev/null \
  || fail "query gateway is not the only bridge between Grafana and the backends"

assert_service_networks() {
  service_name=$1
  shift
  expected_networks=$(printf '%s\n' "$@" | jq -Rsc 'split("\n") | map(select(length > 0)) | sort')
  service_id=$(compose_test ps -q "$service_name")
  actual_networks=$(docker inspect --format '{{json .NetworkSettings.Networks}}' "$service_id" \
    | jq -c 'keys | sort')
  [ "$actual_networks" = "$expected_networks" ] \
    || fail "$service_name has unexpected networks: $actual_networks"
}

assert_service_networks alloy "$backend_network" "$test_network"
assert_service_networks blackbox "$backend_network" "$test_prefix-probes"
assert_service_networks container-exporter "$backend_network"
assert_service_networks loki "$backend_network"
assert_service_networks node-exporter "$backend_network"
assert_service_networks prometheus "$backend_network"

docker exec --user 472:0 "$grafana_id" sh -euc '
  test -r /tmp/grafana-admin-password
  test "$(stat -c %a /tmp/grafana-admin-password)" = 400
  test "$(awk "/^Uid:/ { print \$2 }" /proc/1/status)" = 472
  test "$(awk "/^Gid:/ { print \$2 }" /proc/1/status)" = 0
  test "$(awk "/^CapEff:/ { print \$2 }" /proc/1/status)" = 0000000000000000
  test "$(awk "/^Name:/ { print \$2 }" /proc/1/status)" = grafana
' || fail "Grafana did not drop privileges/capabilities after copying its operator secret"

grafana_binding=$(compose_test port grafana 3000 | tail -n 1)
case "$grafana_binding" in
  127.0.0.1:*) ;;
  *) fail "unexpected Grafana test binding: $grafana_binding" ;;
esac
grafana_url="http://$grafana_binding"

curl --fail --silent --show-error "$grafana_url/" > "$test_root/grafana-home.html"
grep -q '"isSignedIn":false' "$test_root/grafana-home.html" \
  || fail "anonymous Grafana session appears signed in"
grep -q '"orgRole":"Viewer"' "$test_root/grafana-home.html" \
  || fail "anonymous Grafana session is not a Viewer"
grep -q '"isGrafanaAdmin":false' "$test_root/grafana-home.html" \
  || fail "anonymous Grafana session has server administration"
grep -q '"hasEditPermissionInFolders":false' "$test_root/grafana-home.html" \
  || fail "anonymous Grafana session can edit folders"

curl --fail --silent --show-error \
  "$grafana_url/api/dashboards/uid/telemetry-stack-health" \
  > "$test_root/dashboard.json"
jq -e '
  .dashboard.title == "Telemetry stack health"
  and .meta.canSave == false
  and .meta.canEdit == false
  and .meta.canAdmin == false
  and .meta.canDelete == false
  and .meta.provisioned == true
' "$test_root/dashboard.json" >/dev/null \
  || fail "anonymous dashboard permissions are not Viewer-only"

curl --fail --silent --show-error \
  "$grafana_url/api/dashboards/uid/server-and-service-health" \
  > "$test_root/server-dashboard.json"
jq -e '
  .dashboard.title == "Server and service health"
  and .meta.canSave == false
  and .meta.canEdit == false
  and .meta.canAdmin == false
  and .meta.canDelete == false
  and .meta.provisioned == true
' "$test_root/server-dashboard.json" >/dev/null \
  || fail "anonymous server dashboard permissions are not Viewer-only"

curl --fail --silent --show-error \
  "$grafana_url/api/dashboards/uid/family-jobs-board-overview" \
  > "$test_root/application-dashboard.json"
jq -e '
  .dashboard.title == "Family Jobs Board overview"
  and (.dashboard.panels | length) >= 20
  and .meta.canSave == false
  and .meta.canEdit == false
  and .meta.canAdmin == false
  and .meta.canDelete == false
  and .meta.provisioned == true
' "$test_root/application-dashboard.json" >/dev/null \
  || fail "anonymous application dashboard permissions are not Viewer-only"

for dashboard_route in \
  '/d/family-jobs-board-overview/family-jobs-board-overview' \
  '/d/server-and-service-health/server-and-service-health'; do
  curl --fail --silent --show-error "$grafana_url$dashboard_route" \
    > "$test_root/dashboard-route.html" \
    || fail "anonymous dashboard route $dashboard_route did not render"
  grep -q '<title>Grafana</title>' "$test_root/dashboard-route.html" \
    || fail "anonymous dashboard route $dashboard_route did not return the Grafana shell"
done

save_status=$(curl --silent --show-error --output /dev/null \
  --write-out '%{http_code}' \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"dashboard":{"title":"must-not-save"}}' \
  "$grafana_url/api/dashboards/db")
case "$save_status" in
  401|403) ;;
  *) fail "anonymous dashboard persistence returned HTTP $save_status instead of 401/403" ;;
esac

admin_status=$(curl --silent --show-error --output /dev/null \
  --write-out '%{http_code}' "$grafana_url/api/admin/stats")
[ "$admin_status" = "403" ] || fail "anonymous admin endpoint returned HTTP $admin_status instead of 403"

curl --fail --silent --show-error "$grafana_url/api/datasources/uid/prometheus/health" \
  | jq -e '.status == "OK"' >/dev/null \
  || fail "provisioned Prometheus data source is unhealthy"
curl --fail --silent --show-error "$grafana_url/api/datasources/uid/loki/health" \
  | jq -e '.status == "OK"' >/dev/null \
  || fail "provisioned Loki data source is unhealthy"

curl --fail --silent --show-error --get \
  --data-urlencode 'query=up' \
  "$grafana_url/api/datasources/proxy/uid/prometheus/api/v1/query" \
  | jq -e '.status == "success"' >/dev/null \
  || fail "anonymous Prometheus query did not traverse the query gateway"
curl --fail --silent --show-error --get \
  --data-urlencode 'query={service_name=~".+"}' \
  --data-urlencode 'limit=1' \
  "$grafana_url/api/datasources/proxy/uid/loki/loki/api/v1/query_range" \
  | jq -e '.status == "success"' >/dev/null \
  || fail "anonymous Loki query did not traverse the query gateway"

for proxy_write_path in \
  'prometheus/api/v1/otlp/v1/metrics' \
  'prometheus/api/v1/write' \
  'prometheus/api/v1/admin/tsdb/delete_series' \
  'prometheus/-/reload' \
  'loki/otlp/v1/logs' \
  'loki/loki/api/v1/push'; do
  proxy_status=$(curl --silent --show-error --output /dev/null \
    --write-out '%{http_code}' \
    --request POST \
    --header 'Content-Type: application/json' \
    --data '{}' \
    "$grafana_url/api/datasources/proxy/uid/$proxy_write_path")
  case "$proxy_status" in
    403|404|405) ;;
    *) fail "anonymous Grafana datasource proxy write returned HTTP $proxy_status for $proxy_write_path" ;;
  esac
done

gateway_status() {
  request_method=$1
  request_path=$2
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --silent --show-error --output /dev/null \
    --write-out '%{http_code}' \
    --request "$request_method" \
    --header 'Content-Type: application/json' \
    --data '{}' \
    "http://query-gateway:8080$request_path"
}

[ "$(gateway_status POST '/prometheus/api/v1/otlp/v1/metrics')" = "404" ] \
  || fail "query gateway exposed the Prometheus OTLP write endpoint"
[ "$(gateway_status POST '/prometheus/api/v1/write')" = "404" ] \
  || fail "query gateway exposed the Prometheus remote-write endpoint"
[ "$(gateway_status POST '/prometheus/api/v1/admin/tsdb/delete_series')" = "404" ] \
  || fail "query gateway exposed a Prometheus admin endpoint"
[ "$(gateway_status POST '/prometheus/-/reload')" = "404" ] \
  || fail "query gateway exposed the Prometheus reload endpoint"
[ "$(gateway_status POST '/loki/otlp/v1/logs')" = "404" ] \
  || fail "query gateway exposed the Loki OTLP write endpoint"
[ "$(gateway_status POST '/loki/loki/api/v1/push')" = "404" ] \
  || fail "query gateway exposed the Loki push endpoint"
[ "$(gateway_status DELETE '/prometheus/api/v1/query')" = "405" ] \
  || fail "query gateway accepted a disallowed method on a Prometheus query endpoint"
[ "$(gateway_status PUT '/loki/loki/api/v1/query_range')" = "405" ] \
  || fail "query gateway accepted a disallowed method on a Loki query endpoint"

backend_get() {
  request_url=$1
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --fail --silent --show-error "$request_url"
}

backend_prometheus_query() {
  query=$1
  evaluation_time=${2:-}
  request_url=http://prometheus:9090/api/v1/query
  if [ -n "$evaluation_time" ]; then
    request_url="$request_url?time=$evaluation_time"
  fi
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --fail --silent --show-error --get \
    --data-urlencode "query=$query" \
    "$request_url"
}

attempt=0
while :; do
  attempt=$((attempt + 1))
  backend_get 'http://prometheus:9090/api/v1/targets' > "$test_root/targets.json"
  if jq -e '
    (.data.activeTargets | length) >= 15
    and all(.data.activeTargets[]; .health == "up")
  ' "$test_root/targets.json" >/dev/null; then
    break
  fi
  [ "$attempt" -lt 40 ] || fail "Prometheus scrape targets did not all become healthy"
  sleep 2
done

container_exporter_status() {
  request_method=$1
  request_path=$2
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --silent --show-error --output /dev/null \
    --write-out '%{http_code}' \
    --request "$request_method" \
    "http://container-exporter:9400$request_path"
}

[ "$(container_exporter_status GET '/health')" = "200" ] \
  || fail "container exporter health does not verify a live bounded collection"
[ "$(container_exporter_status GET '/metrics')" = "200" ] \
  || fail "container exporter does not expose sanitized metrics"
for denied_request in \
  'GET:/v1.42/containers/json?all=true' \
  'GET:/v1.42/containers/'"$unrelated_fixture_id"'/json' \
  'GET:/v1.42/images/json' \
  'GET:/v1.42/system/df?type=volume' \
  'POST:/metrics' \
  'DELETE:/metrics'; do
  denied_method=${denied_request%%:*}
  denied_path=${denied_request#*:}
  denied_status=$(container_exporter_status "$denied_method" "$denied_path")
  case "$denied_status" in
    404|405|501) ;;
    *) fail "container exporter exposed $denied_method $denied_path (HTTP $denied_status)" ;;
  esac
done

for node_metric in \
  'node_boot_time_seconds%7Bjob%3D%22node-exporter%22%7D' \
  'node_cpu_seconds_total%7Bjob%3D%22node-exporter%22%2Cmode%3D%22idle%22%7D' \
  'node_load1%7Bjob%3D%22node-exporter%22%7D' \
  'node_memory_MemTotal_bytes%7Bjob%3D%22node-exporter%22%7D'; do
  backend_get "http://prometheus:9090/api/v1/query?query=$node_metric" \
    > "$test_root/node-metric.json"
  jq -e '.data.result | length > 0' "$test_root/node-metric.json" >/dev/null \
    || fail "required host metric $node_metric was not scraped"
done
backend_get 'http://prometheus:9090/api/v1/query?query=node_filesystem_size_bytes%7Bjob%3D%22node-exporter%22%7D' \
  > "$test_root/node-filesystem-metric.json"
if ! jq -e '.data.result | length > 0' "$test_root/node-filesystem-metric.json" >/dev/null; then
  docker_operating_system=$(docker info --format '{{.OperatingSystem}}')
  case "$docker_operating_system" in
    *Docker\ Desktop*)
      echo "Skipping host-root filesystem metric assertion on Docker Desktop's Linux VM."
      ;;
    *) fail "required host root-filesystem metric was not scraped" ;;
  esac
fi
for forbidden_node_metric in node_uname_info node_time_seconds; do
  backend_get "http://prometheus:9090/api/v1/query?query=$forbidden_node_metric" \
    > "$test_root/node-metric.json"
  jq -e '.data.result | length == 0' "$test_root/node-metric.json" >/dev/null \
    || fail "privacy-sensitive Node Exporter metric $forbidden_node_metric was scraped"
done

backend_get 'http://prometheus:9090/api/v1/query?query=family_jobs_board_container_collector_success' \
  > "$test_root/container-collector-success.json"
jq -e '(.data.result[0].value[1] | tonumber) == 1' \
  "$test_root/container-collector-success.json" >/dev/null \
  || fail "container collector does not report a successful bounded scrape"
backend_get 'http://prometheus:9090/api/v1/query?query=family_jobs_board_container_running' \
  > "$test_root/container-running.json"
jq -e '
  ([.data.result[] | .metric.service] | sort) == ["api", "db", "proxy", "web"]
  and all(.data.result[]; (.value[1] | tonumber) == 1)
' "$test_root/container-running.json" >/dev/null \
  || fail "container collector did not export only the four expected services"
if grep -q 'unrelated-test-project' "$test_root/container-running.json"; then
  fail "container collector exposed the unrelated labeled container"
fi
backend_get 'http://prometheus:9090/api/v1/query?query=family_jobs_board_container_health' \
  > "$test_root/container-health.json"
jq -e '
  ([.data.result[] | .metric.service] | sort) == ["api", "db", "proxy", "web"]
  and all(.data.result[]; .metric.status == "healthy")
' "$test_root/container-health.json" >/dev/null \
  || fail "container health fixtures were not exported as expected"
backend_get 'http://prometheus:9090/api/v1/query?query=family_jobs_board_container_info' \
  > "$test_root/container-info.json"
jq -e '
  ([.data.result[] | .metric.service] | sort) == ["api", "db", "proxy", "web"]
  and all(.data.result[]; .metric.image_id | test("^sha256:[a-f0-9]{64}$"))
' "$test_root/container-info.json" >/dev/null \
  || fail "container image metrics do not include an immutable content ID"
backend_get 'http://prometheus:9090/api/v1/query?query=family_jobs_board_telemetry_storage_used_bytes' \
  > "$test_root/telemetry-storage.json"
jq -e '
  ([.data.result[] | .metric.store] | sort) == ["loki", "prometheus"]
  and all(.data.result[]; (.value[1] | tonumber) >= 0)
  and all(.data.result[]; (.metric | keys | sort) == ["__name__", "collector", "instance", "job", "store"])
' "$test_root/telemetry-storage.json" >/dev/null \
  || fail "container collector did not export only the two allowlisted telemetry volume sizes"
backend_get 'http://prometheus:9090/api/v1/query?query=probe_success%7Bjob%3D%22family-jobs-board-service-health%22%7D' \
  > "$test_root/application-probes.json"
jq -e '
  ([.data.result[] | .metric.service] | sort) == ["api-live", "api-ready", "web"]
  and all(.data.result[]; (.value[1] | tonumber) == 1)
' "$test_root/application-probes.json" >/dev/null \
  || fail "Family Jobs Board endpoint probes are not all healthy"

backend_get 'http://prometheus:9090/api/v1/query?query=prometheus_tsdb_retention_limit_seconds' \
  > "$test_root/retention-time.json"
jq -e '(.data.result[0].value[1] | tonumber) == 2592000' "$test_root/retention-time.json" >/dev/null \
  || fail "running Prometheus does not report 30-day retention"
backend_get 'http://prometheus:9090/api/v1/query?query=prometheus_tsdb_retention_limit_bytes' \
  > "$test_root/retention-size.json"
jq -e '(.data.result[0].value[1] | tonumber) == 1677721600' "$test_root/retention-size.json" >/dev/null \
  || fail "running Prometheus does not report the 1600 MiB size trigger"

timestamp_nanos="$(date +%s)000000000"
jq --arg ts "$timestamp_nanos" '
  def version_boundary_resource($version; $environment):
    .resourceLogs[0]
    | .resource.attributes |= map(
        if .key == "service.version" then .value.stringValue = $version
        elif .key == "deployment.environment.name" then .value.stringValue = $environment
        else . end)
    | .scopeLogs = [
        (.scopeLogs[0]
          | .logRecords = [
              (.logRecords[0]
                | .body.stringValue = "Application started."
                | .attributes |= map(
                    select(.key as $key | ["telemetry.schema", "service.name", "service.version", "deployment.environment.name", "event.name"] | index($key) != null)
                    | if .key == "service.version" then .value.stringValue = $version
                      elif .key == "deployment.environment.name" then .value.stringValue = $environment
                      elif .key == "event.name" then .value.stringValue = "application.started"
                      else . end))
            ])
      ];

  (.resourceLogs[0].scopeLogs[0].logRecords[0] as $real
    | .resourceLogs[0].scopeLogs[0].logRecords += [
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "PRIVATE BAD STATUS CLASS MUST NOT SURVIVE"
          | .attributes |= map(if .key == "http.response.status_class"
                              then .value.stringValue = "PRIVATE-STATUS"
                              else . end)),
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "HTTP request completed."
          | .attributes |= map(if .key == "duration_ms"
                              then .value.doubleValue = 600000.0
                              else . end)),
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "PRIVATE OVER-LIMIT DURATION MUST NOT SURVIVE"
          | .attributes |= map(if .key == "duration_ms"
                              then .value.doubleValue = 600000.001
                              else . end)),
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "HTTP authentication rejected."
          | .attributes |= map(if .key == "event.name" then .value.stringValue = "authentication.rejected"
                              elif .key == "http.response.status_code" then .value.intValue = "401"
                              elif .key == "http.response.status_class" then .value.stringValue = "4xx"
                              elif .key == "duration_ms" then empty
                              else . end)),
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "Unhandled request failure."
          | .attributes |= map(if .key == "event.name" then .value.stringValue = "http.request.unhandled_failure"
                              elif .key == "http.response.status_code" then .value.intValue = "500"
                              elif .key == "http.response.status_class" then .value.stringValue = "5xx"
                              elif .key == "duration_ms" then empty
                              else . end)),
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "Application started."
          | .attributes |= map(select(.key as $key | ["telemetry.schema", "service.name", "service.version", "deployment.environment.name", "event.name"] | index($key) != null)
                              | if .key == "event.name" then .value.stringValue = "application.started"
                                else . end)),
        ($real
          | .timeUnixNano = $ts
          | .observedTimeUnixNano = $ts
          | .body.stringValue = "Invalid telemetry exporter configuration was ignored."
          | .attributes |= map(select(.key as $key | ["telemetry.schema", "service.name", "service.version", "deployment.environment.name", "event.name"] | index($key) != null)
                              | if .key == "event.name" then .value.stringValue = "telemetry.configuration.ignored"
                                else . end)
          | .attributes += [{"key":"reason","value":{"stringValue":"invalid_endpoint"}}])
      ])
  | .resourceLogs += [
      version_boundary_resource("1.release-4"; "test"),
      version_boundary_resource(".release"; "development"),
      version_boundary_resource("-release"; "staging"),
      version_boundary_resource("_release"; "local")
    ]
  | (.resourceLogs[].scopeLogs[].logRecords[] | .timeUnixNano, .observedTimeUnixNano) = $ts
' observability/tests/fixtures/otlp-logs.json > "$test_root/fixtures/otlp-logs.json"
jq --arg ts "$timestamp_nanos" '
  (.resourceMetrics[0].scopeMetrics[0].metrics[0].histogram.dataPoints[0] as $matched
    | .resourceMetrics[0].scopeMetrics[0].metrics[0].histogram.dataPoints += [
        ($matched
          | .attributes |= map(select(.key != "http.route")
                              | if .key == "http.response.status_code"
                                then .value.intValue = "404"
                                else . end)),
        ($matched
          | .attributes |= map(if .key == "http.request.method"
                              then .value.stringValue = "POST"
                              elif .key == "http.route"
                              then .value.stringValue = "/api/auth/sign-in"
                              elif .key == "http.response.status_code"
                              then .value.intValue = "401"
                              else . end)),
        ($matched
          | .attributes |= map(if .key == "http.request.method"
                              then .value.stringValue = "GET"
                              elif .key == "http.route"
                              then .value.stringValue = "/api/today"
                              elif .key == "http.response.status_code"
                              then .value.intValue = "500"
                              else . end))
      ])
  |
  (.resourceMetrics[].scopeMetrics[].metrics[] |
    (.histogram.dataPoints[]?, .gauge.dataPoints[]?, .sum.dataPoints[]?) |
    .timeUnixNano) = $ts
  |
  (.resourceMetrics[].scopeMetrics[].metrics[] |
    (.histogram.dataPoints[]?, .sum.dataPoints[]?) |
    .startTimeUnixNano) = $ts
' observability/tests/fixtures/otlp-metrics.json > "$test_root/fixtures/otlp-metrics.json"
chmod 0644 "$test_root/fixtures/otlp-logs.json" "$test_root/fixtures/otlp-metrics.json"

push_otlp_fixture() {
  signal=$1
  docker run --rm --network "$test_network" \
    -v "$test_root/fixtures:/fixtures:ro" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --fail-with-body --silent --show-error \
    --header 'Content-Type: application/json' \
    --data-binary "@/fixtures/otlp-$signal.json" \
    "http://alloy:4318/v1/$signal" \
    | jq -e '.partialSuccess == {}' >/dev/null
}

refresh_metric_fixture() {
  refreshed_timestamp_nanos="$(($(date +%s) * 1000000000))"
  jq --arg ts "$refreshed_timestamp_nanos" '
    (.resourceMetrics[].scopeMetrics[].metrics[] |
      (.histogram.dataPoints[]?, .gauge.dataPoints[]?, .sum.dataPoints[]?) |
      .timeUnixNano) = $ts
  ' "$test_root/fixtures/otlp-metrics.json" \
    > "$test_root/fixtures/otlp-metrics-refreshed.json"
  mv "$test_root/fixtures/otlp-metrics-refreshed.json" \
    "$test_root/fixtures/otlp-metrics.json"
  chmod 0644 "$test_root/fixtures/otlp-metrics.json"
}

for signal in logs metrics; do
  push_otlp_fixture "$signal" \
    || fail "Alloy rejected the synthetic OTLP $signal fixture"
done

# Verify the read-only backup recipe can read service-owned Loki and Alloy data
# while writing archives to a root-owned host directory.
docker volume create "$backup_volume" >/dev/null
for service_name in loki alloy; do
  docker run --rm --network none --read-only --cap-drop ALL \
    --cap-add DAC_READ_SEARCH \
    -v "$test_prefix-$service_name-data:/source:ro" \
    -v "$backup_volume:/backup" \
    busybox:1.37.0-uclibc tar czf "/backup/$service_name-data.tgz" -C /source .
  docker run --rm --network none -v "$backup_volume:/backup:ro" \
    busybox:1.37.0-uclibc test -s "/backup/$service_name-data.tgz" \
    || fail "$service_name backup archive was not written"
done

attempt=0
while :; do
  attempt=$((attempt + 1))
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --fail --silent --show-error --get \
    --data-urlencode 'match[]={job="family-jobs-board-api"}' \
    http://prometheus:9090/api/v1/series \
    > "$test_root/application-series.json"

  if jq -e '
    [.data[].__name__] as $names
    | ($names | index("http_server_request_duration_seconds_count") != null)
      and ($names | index("aspnetcore_diagnostics_exceptions_total") != null)
      and ($names | index("family_jobs_board_application_runtime_error_count_total") != null)
      and ($names | index("family_jobs_board_application_warning_count_total") != null)
      and ($names | index("family_jobs_board_authentication_rejection_count_total") != null)
      and ($names | index("family_jobs_board_process_start_time_seconds") != null)
      and ($names | index("dotnet_process_cpu_count") != null)
      and any($names[]; startswith("dotnet_gc_collections"))
      and any($names[]; startswith("dotnet_process_cpu_time"))
  ' "$test_root/application-series.json" >/dev/null; then
    break
  fi
  if [ "$attempt" -ge 30 ]; then
    observed_metrics=$(jq -r '[.data[].__name__] | unique | join(", ")' "$test_root/application-series.json")
    fail "allowed OTLP metrics did not reach Prometheus (observed: ${observed_metrics:-none})"
  fi
  sleep 1
done

jq -e '
  [.data[]
    | select(.__name__ == "aspnetcore_diagnostics_exceptions_total")
    | .aspnetcore_diagnostics_exception_result] as $results
  | ($results | length) == 4
    and (["handled", "unhandled", "skipped", "aborted"]
      | all(.[]; . as $result | $results | index($result) != null))
' "$test_root/application-series.json" >/dev/null \
  || fail "stable ASP.NET Core exception outcomes were dropped or widened"
jq -e '
  [.data[] | select(.__name__ | startswith("dotnet_gc_collections")) | .gc_heap_generation]
    | index("gen0") != null and index("gen1") != null
' "$test_root/application-series.json" >/dev/null \
  || fail "bounded GC-generation points collapsed or were removed"
jq -e '
  [.data[] | select(.__name__ | startswith("dotnet_process_cpu_time")) | .cpu_mode]
    | index("user") != null and index("system") != null
' "$test_root/application-series.json" >/dev/null \
  || fail "bounded CPU-mode points collapsed or were removed"
jq -e '
  [.data[] | select(.__name__ == "http_server_request_duration_seconds_count") | .http_request_method]
    | index("GET") != null
      and index("CONNECT") != null
      and index("TRACE") != null
      and index("_OTHER") != null
' "$test_root/application-series.json" >/dev/null \
  || fail "bounded HTTP-method points collapsed or were removed"
jq -e '
  [.data[] | select(.__name__ == "http_server_request_duration_seconds_count") | .http_route] as $routes
  | ($routes | index("/api/users/")) != null
    and ($routes | index("/api/turn-rotations/")) != null
    and any(.data[];
      .__name__ == "http_server_request_duration_seconds_count"
      and .http_request_method == "GET"
      and .http_response_status_code == "404"
      and .http_route == null)
' "$test_root/application-series.json" >/dev/null \
  || fail "group-root or unmatched-route request points were dropped"
jq -e '
  ["0.005", "0.01", "0.025", "0.05", "0.1", "0.25", "0.5", "1", "2.5", "5", "10", "+Inf"] as $allowed
  | [.data[] | select(.__name__ == "http_server_request_duration_seconds_bucket") | .le] as $actual
  | ($actual | length) > 0
    and all($actual[]; . as $value | $allowed | index($value) != null)
' "$test_root/application-series.json" >/dev/null \
  || fail "Prometheus retained hostile or unexpected HTTP histogram boundaries"

jq -e '
  [
    "__name__", "aspnetcore_diagnostics_exception_result", "cpu_mode",
    "deployment_environment_name", "event_severity", "gc_heap_generation",
    "http_request_method", "http_response_status_code", "http_route", "job",
    "le", "service_name"
  ] as $allowed
  | all(.data[]; all(keys[]; . as $key | $allowed | index($key) != null))
' "$test_root/application-series.json" >/dev/null \
  || fail "Prometheus retained a non-allowlisted application label"

assert_prometheus_value() {
  query=$1
  expected=$2
  description=$3
  backend_prometheus_query "$query" > "$test_root/dashboard-metric.json"
  jq -e --argjson expected "$expected" '
    .status == "success"
    and (.data.result | length) == 1
    and ((.data.result[0].value[1] | tonumber) == $expected)
  ' "$test_root/dashboard-metric.json" >/dev/null \
    || fail "$description did not equal $expected"
}

wait_for_prometheus_value() {
  query=$1
  expected=$2
  description=$3
  output_file=$4
  attempt=0
  while :; do
    attempt=$((attempt + 1))
    backend_prometheus_query "$query" > "$output_file"
    if jq -e --arg expected "$expected" \
      '.data.result[0].value[1]? == $expected' \
      "$output_file" >/dev/null; then
      return
    fi
    [ "$attempt" -lt 40 ] || fail "$description"
    sleep 2
  done
}

assert_prometheus_value \
  'sum(family_jobs_board_application_runtime_error_count_total{service_name="family-jobs-board-api"})' \
  1 \
  "runtime error count"
assert_prometheus_value \
  'sum(family_jobs_board_application_warning_count_total{service_name="family-jobs-board-api"})' \
  1 \
  "warning count"
assert_prometheus_value \
  'sum(family_jobs_board_authentication_rejection_count_total{service_name="family-jobs-board-api",http_response_status_code="401"})' \
  1 \
  "authentication rejection count"
assert_prometheus_value \
  'sum(aspnetcore_diagnostics_exceptions_total{service_name="family-jobs-board-api",aspnetcore_diagnostics_exception_result="unhandled"})' \
  1 \
  "unhandled exception count"
assert_prometheus_value \
  'sum(http_server_request_duration_seconds_count{service_name="family-jobs-board-api",http_response_status_code=~"4.."})' \
  2 \
  "HTTP 4xx count"

# Four allowlisted base request points plus the three valid synthetic request
# points survive; the two hostile histogram/route fixtures are rejected.
for panel_expectation in 3:1 4:1 5:1 6:1 10:7 20:2; do
  panel_id=${panel_expectation%%:*}
  expected=${panel_expectation#*:}
  panel_title=$(jq -r --argjson panel_id "$panel_id" \
    '.panels[] | select(.id == $panel_id) | .title' \
    observability/config/grafana/dashboards/family-jobs-board-overview.json)
  panel_query=$(jq -r --argjson panel_id "$panel_id" \
    '.panels[] | select(.id == $panel_id) | .targets[0].expr' \
    observability/config/grafana/dashboards/family-jobs-board-overview.json)
  backend_prometheus_query "$panel_query" > "$test_root/process-count-$panel_id.json"
  jq -e --argjson expected "$expected" '
    .status == "success"
    and (.data.result | length) == 1
    and ((.data.result[0].value[1] | tonumber) == $expected)
  ' "$test_root/process-count-$panel_id.json" >/dev/null \
    || fail "$panel_title did not report the exact expected $expected current-process events"
done

requests_query=$(jq -r \
  '.panels[] | select(.id == 10) | .targets[0].expr' \
  observability/config/grafana/dashboards/family-jobs-board-overview.json)
requests_zero_query=$(printf '%s\n' "$requests_query" \
  | sed 's/http_server_request_duration_seconds_count/missing_request_count_total/g')
assert_prometheus_value \
  "$requests_zero_query" \
  0 \
  "fresh process request zero synthesis"

four_xx_query=$(jq -r \
  '.panels[] | select(.id == 20) | .targets[0].expr' \
  observability/config/grafana/dashboards/family-jobs-board-overview.json)
four_xx_zero_query=$(printf '%s\n' "$four_xx_query" \
  | sed 's/http_response_status_code=~"4\.\."/http_response_status_code="418"/g')
assert_prometheus_value \
  "$four_xx_zero_query" \
  0 \
  "fresh process HTTP 4xx zero synthesis"

for panel_id in 3 10 20; do
  panel_query=$(jq -r --argjson panel_id "$panel_id" \
    '.panels[] | select(.id == $panel_id) | .targets[0].expr' \
    observability/config/grafana/dashboards/family-jobs-board-overview.json)
  stale_query=$(printf '%s\n' "$panel_query" \
    | sed 's/timestamp(family_jobs_board_process_start_time_seconds{service_name="family-jobs-board-api"})/(timestamp(family_jobs_board_process_start_time_seconds{service_name="family-jobs-board-api"}) - 180)/g')
  backend_prometheus_query "$stale_query" > "$test_root/stale-process-count-$panel_id.json"
  jq -e '.status == "success" and (.data.result | length) == 0' \
    "$test_root/stale-process-count-$panel_id.json" >/dev/null \
    || fail "stale current-process panel $panel_id did not render NO DATA"
done

# Prometheus can retain the previous process' last counter sample for several
# minutes after a restart. Model a fresh, later process-start gauge without new
# counter samples and require every headline to discard the old process' value.
for panel_id in 3 4 5 6 10 20; do
  panel_query=$(jq -r --argjson panel_id "$panel_id" \
    '.panels[] | select(.id == $panel_id) | .targets[0].expr' \
    observability/config/grafana/dashboards/family-jobs-board-overview.json)
  restarted_query=$(printf '%s\n' "$panel_query" \
    | sed 's/family_jobs_board_process_start_time_seconds{service_name="family-jobs-board-api"}/vector(time() + 60)/g')
  assert_prometheus_value \
    "$restarted_query" \
    0 \
    "post-restart current-process panel $panel_id zero synthesis"
done

if jq -e '
  [.data[].__name__] as $names
  | any($names[];
      . == "private_household_member_count"
      or startswith("dotnet_exceptions"))
' "$test_root/application-series.json" >/dev/null; then
  fail "Prometheus retained a rejected metric or malformed allowed point"
fi
if grep -Eq 'must-not-survive|Private\.Exception|member_(id|name)|process_command_line|service_instance_id' \
  "$test_root/application-series.json"; then
  fail "Prometheus retained a private resource or data-point attribute"
fi
backend_get 'http://prometheus:9090/api/v1/metadata?metric=http_server_request_duration_seconds' \
  > "$test_root/application-metadata.json"
if grep -Eq 'PRIVATE|must-not-survive' "$test_root/application-metadata.json"; then
  fail "Prometheus retained private metric metadata"
fi

attempt=0
while :; do
  attempt=$((attempt + 1))
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --fail --silent --show-error --get \
    --data-urlencode 'query={service_name="family-jobs-board-api"} | event_name="http.request.completed"' \
    --data-urlencode 'limit=20' \
    http://loki:3100/loki/api/v1/query_range \
    > "$test_root/application-logs.json"
  if jq -e '[.data.result[].values[]] | length == 2' "$test_root/application-logs.json" >/dev/null; then
    break
  fi
  [ "$attempt" -lt 30 ] || fail "sanitized OTLP event did not reach Loki"
  sleep 1
done

jq -e '
  .data.result as $result
  | ([ $result[].values[] ] | length == 2)
    and all($result[];
      .stream.service_name == "family-jobs-board-api"
      and .stream.scope_name == "FamilyJobsBoard.Telemetry"
      and .stream.telemetry_schema == "family-jobs-board.event.v1"
      and .stream.event_name == "http.request.completed"
      and .stream.http_request_method == "_OTHER"
      and all(.values[]; .[1] == "http.request.completed"))
    and ([ $result[].stream.duration_ms | tonumber ] | sort) == [125, 600000]
' "$test_root/application-logs.json" >/dev/null \
  || fail "Loki did not enforce the sanitized event and duration boundary contract"

jq -e '
  [
    "deployment_environment_name", "detected_level", "duration_ms", "event_name",
    "http_request_method", "http_response_status_code", "http_route",
    "observed_timestamp", "scope_name", "scope_version", "service_name",
    "severity_number", "severity_text", "telemetry_schema"
  ] as $allowed
  | .data.result
  | all(.[]; all(.stream | keys[]; . as $key | $allowed | index($key) != null))
' "$test_root/application-logs.json" >/dev/null \
  || fail "Loki retained a non-allowlisted event field"

if grep -Eq 'must-not-survive|PRIVATE|REJECTED|IdentityAudit|identity\.audit|application\.warning|member_id|request_id|session_id|trace_id|span_id|reason|314159265|271828182|161803398|dropped_attributes_count|scope_dropped_attributes_count' \
  "$test_root/application-logs.json"; then
  fail "Loki retained a private body, identifier, envelope field, or rejected event"
fi
jq -e '
  all(.data.result[];
    .stream.severity_text == "INFO"
    and .stream.severity_number == "9")
' "$test_root/application-logs.json" >/dev/null \
  || fail "Loki did not canonicalize the accepted event severity"

for event_name in authentication.rejected http.request.unhandled_failure application.started telemetry.configuration.ignored; do
  docker run --rm --network "$backend_network" \
    --entrypoint /usr/bin/curl \
    grafana/grafana:13.2.3 \
    --fail --silent --show-error --get \
    --data-urlencode "query={service_name=\"family-jobs-board-api\"} | event_name=\"$event_name\"" \
    --data-urlencode 'limit=10' \
    http://loki:3100/loki/api/v1/query_range \
    > "$test_root/event-$event_name.json"
  if [ "$event_name" = "application.started" ]; then
    jq -e --arg event_name "$event_name" '
      [.data.result[] | .values[]] as $values
      | [.data.result[].stream.deployment_environment_name] as $environments
      | ($values | length) == 2
        and ($environments | sort) == ["test", "testing"]
        and all(.data.result[];
          .stream.event_name == $event_name
          and (.values | length) == 1)
    ' "$test_root/event-$event_name.json" >/dev/null \
      || fail "Alloy did not accept alphanumeric-leading and reject punctuation-leading service versions"
  else
    jq -e --arg event_name "$event_name" \
      '.data.result | length == 1 and .[0].stream.event_name == $event_name and (.[0].values | length == 1)' \
      "$test_root/event-$event_name.json" >/dev/null \
      || fail "Alloy did not accept and sanitize the real $event_name attribute shape"
  fi
done

overall_state_query=$(jq -r \
  '.panels[] | select(.id == 1) | .targets[0].expr' \
  observability/config/grafana/dashboards/family-jobs-board-overview.json)
refresh_metric_fixture
push_otlp_fixture metrics \
  || fail "Alloy rejected the refreshed synthetic OTLP metrics fixture"
wait_for_prometheus_value \
  "$overall_state_query" \
  2 \
  "healthy dashboard state" \
  "$test_root/healthy-dashboard-state.json"

storage_degraded_query=$(printf '%s\n' "$overall_state_query" \
  | sed 's/family_jobs_board_telemetry_storage_collector_success/vector(0)/g')
assert_prometheus_value \
  "$storage_degraded_query" \
  1 \
  "storage-usage failure dashboard state"

docker stop "$api_fixture_id" >/dev/null
wait_for_prometheus_value \
  'family_jobs_board_container_running{service="api"}' \
  0 \
  "stopped API fixture did not become unavailable in dashboard telemetry" \
  "$test_root/api-running.json"
assert_prometheus_value "$overall_state_query" 0 "unhealthy dashboard state"

docker start "$api_fixture_id" >/dev/null
wait_for_prometheus_value \
  'family_jobs_board_container_health{service="api",status="healthy"}' \
  1 \
  "restarted API fixture did not recover in dashboard telemetry" \
  "$test_root/api-health.json"
assert_prometheus_value "$overall_state_query" 2 "recovered dashboard state"

application_stale_query=$(printf '%s\n' "$overall_state_query" \
  | sed 's/timestamp(family_jobs_board_process_start_time_seconds{service_name="family-jobs-board-api"})/(timestamp(family_jobs_board_process_start_time_seconds{service_name="family-jobs-board-api"}) - 180)/g')
assert_prometheus_value "$application_stale_query" 3 "application-only stale dashboard state"

collector_stale_query=$(printf '%s\n' "$overall_state_query" \
  | sed 's/family_jobs_board_container_collector_last_success_timestamp_seconds/(family_jobs_board_container_collector_last_success_timestamp_seconds - 180)/g')
assert_prometheus_value "$collector_stale_query" 3 "collector-only stale dashboard state"

collector_stale_history_query=$(jq -r '
  .panels[]
  | select(.id == 17)
  | .targets[]
  | select(.refId == "D")
  | .expr
' observability/config/grafana/dashboards/family-jobs-board-overview.json \
  | sed 's/family_jobs_board_container_collector_last_success_timestamp_seconds/(family_jobs_board_container_collector_last_success_timestamp_seconds - 180)/g')
assert_prometheus_value \
  "$collector_stale_history_query" \
  1 \
  "collector stale-period history"

compose_test stop container-exporter >/dev/null
wait_for_prometheus_value \
  'up{job="family-jobs-board-containers"}' \
  0 \
  "stopped container collector did not become unavailable" \
  "$test_root/container-collector-down.json"
assert_prometheus_value "$overall_state_query" 0 "collector-down dashboard state"
compose_test start container-exporter >/dev/null
wait_for_prometheus_value \
  'up{job="family-jobs-board-containers"}' \
  1 \
  "restarted container collector did not recover" \
  "$test_root/container-collector-recovered.json"
wait_for_prometheus_value \
  'family_jobs_board_telemetry_storage_collector_success' \
  1 \
  "restarted container collector did not refresh telemetry storage usage" \
  "$test_root/storage-collector-recovered.json"
refresh_metric_fixture
push_otlp_fixture metrics \
  || fail "Alloy rejected the recovery OTLP metrics fixture"
wait_for_prometheus_value \
  "$overall_state_query" \
  2 \
  "collector-recovered dashboard state did not return to healthy" \
  "$test_root/collector-recovered-dashboard-state.json"

future_query_time=$(($(date +%s) + 180))
backend_prometheus_query "$overall_state_query" "$future_query_time" \
  > "$test_root/stale-dashboard-state.json"
jq -e '
  .status == "success"
  and (.data.result | length) == 1
  and ((.data.result[0].value[1] | tonumber) == 3)
' "$test_root/stale-dashboard-state.json" >/dev/null \
  || fail "dashboard did not distinguish stale telemetry from healthy zero"

cpu_query=$(jq -r \
  '.panels[] | select(.id == 3) | .targets[0].expr' \
  observability/config/grafana/dashboards/server-and-service-health.json)
backend_prometheus_query "$cpu_query" > "$test_root/current-host-capacity.json"
jq -e '.status == "success" and (.data.result | length) == 1' \
  "$test_root/current-host-capacity.json" >/dev/null \
  || fail "current host capacity was unavailable"
backend_prometheus_query "$cpu_query" "$future_query_time" \
  > "$test_root/stale-host-capacity.json"
jq -e '.status == "success" and (.data.result | length) == 0' \
  "$test_root/stale-host-capacity.json" >/dev/null \
  || fail "stale host capacity remained green instead of rendering NO DATA"

old_api_image_tag=$(jq -r \
  '.data.result[] | select(.metric.service == "api") | .metric.image_tag' \
  "$test_root/container-info.json")
deployment_fixture_image="$test_prefix-api:deployment-2"
docker image tag busybox:1.37.0-uclibc "$deployment_fixture_image"
sleep 1
deployment_fixture_id=$(docker run --detach \
  --label "com.docker.compose.project=$FJB_COMPOSE_PROJECT_NAME" \
  --label 'com.docker.compose.service=api' \
  --health-cmd 'true' \
  --health-interval 1s \
  --health-retries 2 \
  "$deployment_fixture_image" sleep 600)
fixture_containers="$fixture_containers $deployment_fixture_id"
wait_for_prometheus_value \
  'family_jobs_board_container_info{service="api",image_tag="deployment-2"}' \
  1 \
  "replacement API image did not appear in dashboard telemetry" \
  "$test_root/deployment-image.json"
docker run --rm --network "$backend_network" \
  --entrypoint /usr/bin/curl \
  grafana/grafana:13.2.3 \
  --fail --silent --show-error --get \
  --data-urlencode 'match[]=family_jobs_board_container_info{service="api"}' \
  http://prometheus:9090/api/v1/series \
  > "$test_root/deployment-series.json"
jq -e --arg old "$old_api_image_tag" '
  [.data[].image_tag] as $tags
  | ($tags | index($old) != null)
    and ($tags | index("deployment-2") != null)
' "$test_root/deployment-series.json" >/dev/null \
  || fail "dashboard telemetry did not retain both sides of an image deployment change"

smoke_dashboard_queries() {
  dashboard_file=$1
  dashboard_name=$(basename "$dashboard_file")
  query_file="$test_root/$(basename "$dashboard_file").queries"
  jq -r '
    .panels[]
    | .title as $title
    | .datasource.uid as $datasource
    | .targets[]?
    | [$datasource, (.range // false), $title, .expr]
    | @tsv
  ' "$dashboard_file" > "$query_file"

  range_end=$(date +%s)
  range_start=$((range_end - 600))
  tab=$(printf '\t')
  while IFS="$tab" read -r datasource is_range panel_title expression; do
    concrete_expression=$(printf '%s\n' "$expression" \
      | sed 's/\$__range/10m/g; s/\$__rate_interval/1m/g; s/\$__interval/1m/g')
    case "$datasource" in
      prometheus)
        if [ "$is_range" = "true" ]; then
          curl --fail --silent --show-error --get \
            --data-urlencode "query=$concrete_expression" \
            --data-urlencode "start=$range_start" \
            --data-urlencode "end=$range_end" \
            --data-urlencode 'step=15' \
            "$grafana_url/api/datasources/proxy/uid/prometheus/api/v1/query_range" \
            > "$test_root/dashboard-query.json"
        else
          curl --fail --silent --show-error --get \
            --data-urlencode "query=$concrete_expression" \
            "$grafana_url/api/datasources/proxy/uid/prometheus/api/v1/query" \
            > "$test_root/dashboard-query.json"
        fi
        ;;
      loki)
        curl --fail --silent --show-error --get \
          --data-urlencode "query=$concrete_expression" \
          --data-urlencode 'limit=100' \
          --data-urlencode "start=${range_start}000000000" \
          --data-urlencode "end=${range_end}000000000" \
          "$grafana_url/api/datasources/proxy/uid/loki/loki/api/v1/query_range" \
          > "$test_root/dashboard-query.json"
        ;;
      *) fail "dashboard uses unexpected data source $datasource" ;;
    esac
    jq -e '.status == "success"' "$test_root/dashboard-query.json" >/dev/null \
      || fail "dashboard query failed for $datasource: $concrete_expression"
    case "$dashboard_name:$panel_title" in
      family-jobs-board-overview.json:Overall\ state|\
      family-jobs-board-overview.json:Service\ readiness|\
      family-jobs-board-overview.json:Running\ containers|\
      server-and-service-health.json:Host\ uptime|\
      server-and-service-health.json:CPU\ utilisation|\
      server-and-service-health.json:Memory\ used|\
      server-and-service-health.json:Host\ load)
        jq -e '.data.result | length > 0' "$test_root/dashboard-query.json" >/dev/null \
          || fail "$panel_title returned no representative data through Grafana"
        ;;
    esac
  done < "$query_file"
}

smoke_dashboard_queries \
  observability/config/grafana/dashboards/family-jobs-board-overview.json
smoke_dashboard_queries \
  observability/config/grafana/dashboards/server-and-service-health.json

echo "Observability integration checks passed: dashboards rendered read-only, representative queries passed, state transitions and deployments were visible, retention active, and hostile OTLP sanitized."
