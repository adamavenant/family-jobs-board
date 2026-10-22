# Observability stack operations

This runbook operates the separate Grafana/Prometheus/Loki/Alloy/Blackbox/query
gateway Compose project. It does not deploy the Family Jobs Board application,
change DNS or reverse-proxy configuration, or expose Grafana to the LAN.

## Prepare configuration

Install Docker Engine 23.0 or newer with the Compose plugin. The bounded
container collector uses Docker Engine API v1.42, the first API level whose
`GET /system/df?type=volume` request limits disk-usage work to volumes. Copy the
example outside the checkout and create a strong Grafana
operator password as a separate mode-0600 file:

```sh
sudo install -o root -g docker -m 750 -d /etc/family-jobs-board-observability
sudo install -o root -g docker -m 640 observability/environment.example \
  /etc/family-jobs-board-observability/observability.env
sudo sh -c 'umask 077; openssl rand -base64 48 > /etc/family-jobs-board-observability/grafana-admin-password'
sudo sed -i "s/^DOCKER_GID=.*/DOCKER_GID=$(stat -c '%g' /var/run/docker.sock)/" \
  /etc/family-jobs-board-observability/observability.env
```

The environment file contains no credential; the Grafana password remains in
its separate root-only mode-0600 file. The Compose operator must be in the
`docker` group to read the environment file and operate the Docker daemon. A
membership in that group already grants root-equivalent host access. Anonymous
users are Viewers and cannot edit dashboards or administer Grafana. Never
commit either file.

`DOCKER_GID` is the numeric group owner of `/var/run/docker.sock`; recompute it
if Docker is reinstalled or its socket ownership changes. The value lets the
unprivileged container-exporter process open the socket without making any
observability container privileged.

Run the network preflight before either Compose project uses telemetry:

```sh
sudo sh -c 'set -eu; set -a; . /etc/family-jobs-board-observability/observability.env; set +a; exec ./scripts/ensure-observability-network.sh'
```

It creates, or validates, the external `family-jobs-board-telemetry` network as
an internal local bridge. Both Compose projects treat the network as external,
so stopping either project cannot remove it. The application must use its
optional telemetry override; its base Compose project does not depend on this
network or collector.

## Provision bounded production storage

Ordinary Docker named volumes have no intrinsic size limit. Do not use the
base Compose model for a production claim of bounded storage. Before a
Serendipity deployment, provision two dedicated local POSIX filesystems (not
NFS) and mount them at the paths in the environment file:

| Path | Maximum filesystem capacity | Purpose |
| --- | ---: | --- |
| `PROMETHEUS_DATA_DIR` | 2048 MiB | TSDB, WAL, and compaction headroom |
| `LOKI_DATA_DIR` | 4096 MiB | chunks, index, WAL, and Compactor markers |

The deployment owner chooses the host's quota/LVM/loopback/filesystem mechanism
and records it in the deployment repository. That host-level provisioning is
deliberately not automated here. Both paths must already exist and each must be
the root of its own capped filesystem. A startup preflight reads the filesystem
capacity with `df` and rejects a path whose reported capacity exceeds its
budget. This fail-closed check prevents a directory on Serendipity's general
root filesystem from being mistaken for a quota.

The Prometheus `1600MB` setting is parsed as a 1600 MiB size-retention trigger,
below 80% of the 2048 MiB
filesystem. Prometheus may temporarily exceed that trigger while compacting,
so the filesystem boundary—not the retention setting—is the hard cap. Loki's
720-hour retention is time-based; its 4096 MiB filesystem is its hard cap.

## Validate and start

From the repository root, validate all generated configuration before a
change:

```sh
./scripts/check-observability-config.sh
```

Pull the directly used images and rebuild the four health-check wrapper images
plus the privacy-bounded container exporter from their pinned bases:

```sh
docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  pull grafana prometheus query-gateway storage-init

docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  build --pull
```

Production startup must include the bounded-storage overlay:

```sh
sudo sh -c 'set -eu; set -a; . /etc/family-jobs-board-observability/observability.env; set +a; exec ./scripts/ensure-observability-network.sh'

docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  up --detach --wait
```

The one-shot storage preflight and ownership initializer must exit successfully;
Grafana, Prometheus, Loki, Alloy, Blackbox, Node Exporter, the container
exporter, and the query gateway must report healthy:

```sh
docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  ps --all
```

No service is reachable from the host in this issue. The
`compose.test-ingress.yaml` file publishes Grafana only on loopback for the
automated verification harness; never include it in a LAN deployment.
Grafana has no route to the backend network. Its data sources use the internal
query gateway, whose deny-by-default path-and-method allowlist exposes only
reviewed Prometheus/Loki query APIs. OTLP ingest and backend administration stay
reachable only from the collector/backend side, never through the anonymous
Grafana data-source proxy.

Grafana opens the read-only **Family Jobs Board overview** by default. It shows
the current application state, telemetry freshness, runtime-error and warning
counts, expected authentication/4xx outcomes, traffic and latency, noteworthy
aggregate events, running containers, and deployment image identity. Use the
dashboard links to keep the same time range while moving to **Server and
service health** or the lower-level **Telemetry stack health** diagnostics.
The default range is six hours; the picker includes the full 30-day retention
window.

`NO DATA` means a required series is absent. `STALE` means a required sample is
more than two minutes old. Neither is a healthy zero. A migration failure is
visible indirectly as API readiness/startup failure because the one-shot
migration service is deliberately outside the bounded runtime-container
exporter. Headline counters include only samples from the current API process,
so a restart cannot briefly replay the previous process' final totals. A
confirmed endpoint or container-collector outage remains `UNHEALTHY` even when
application or container detail later ages out. Volume-size collection fails
independently as `DEGRADED` and does not hide current container health.

## Host, service, and container health

The **Server and service health** dashboard is a separate provisioned Grafana
dashboard. It intentionally contains only:

- Serendipity CPU, load, memory, uptime, root-filesystem byte capacity, and root
  inode pressure from Node Exporter;
- HTTP checks named **Service health** for the public web endpoint and API
  liveness/readiness endpoints at `dashboard.home.arpa`; and
- running/state, Docker health, restart count, OOM-killed flag, start/age, and
  bounded image tag and immutable Docker image content ID for the `api`, `db`,
  `proxy`, and `web` Compose services.

It also shows Docker-reported bytes used by the exact Prometheus and Loki data
volumes. This includes every file in each volume, while the capacity checks
below remain authoritative for filesystem size and non-volume overhead.
Per-container CPU and memory remain deliberately excluded: the approved scope
is aggregate server capacity, and collecting per-container resources would
require a broader Docker/cAdvisor boundary.

Node Exporter runs without host PID/network namespaces, without privilege, and
with every default collector disabled. Its explicit collector allowlist does
not expose host processes, environment variables, network connections, or file
contents. Read-only `/proc`, `/sys`, and root mounts are nevertheless sensitive
host access: the root mount exists only so the filesystem collector can call
`statfs` for `/`. Keep the image pin and collector flags under review.

The container exporter is the only service that receives the read-only Docker
socket bind. Its Docker client has a fixed `GET` allowlist for container list
and inspect plus volume disk usage, sends exact Compose project/service label
filters, rechecks returned labels, and emits only the fixed service name plus
sanitized image tag and content ID. From the disk-usage response it retains
only byte totals for the exact observability Prometheus and Loki volume names;
unrelated volume names and sizes are discarded. Docker's host-wide disk-usage
call is cached for five minutes rather than repeated by each health check and
scrape, and separate success/last-success metrics make a failed refresh stale.
Its network surface exposes
only `/metrics` and a fail-closed `/health`; raw Docker endpoints are not
proxied. Container names, IDs, arbitrary labels, environment, mounts, commands,
logs, and unrelated volume inventory never become metrics.

The Docker socket remains a root-equivalent interface despite its read-only
bind flag; the flag protects the socket file, not the HTTP methods sent over
it. A compromise of the exporter process could therefore bypass its own
request policy. Its no-capabilities/read-only/no-new-privileges baseline,
unprivileged socket-group identity, fixed API path and service allowlists,
sanitized output, and private backend network reduce but do not remove that
residual risk. Never attach the Docker socket to Grafana, Prometheus, Loki,
Blackbox, or Alloy.

Blackbox alone has an egress-capable probe network so it can resolve and reach
`dashboard.home.arpa`; it has no host port. If all three application checks go
down together, verify that Docker's configured DNS can resolve the LAN-only
name before treating the result as an application failure.

## Stop, restart, and remove non-destructively

`stop` retains containers, networks, and every telemetry volume:

```sh
docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  stop
```

Start the same containers with `start`, or rerun `up --detach --wait` after a
configuration change. To remove containers and the project-private backend and
query networks while preserving all data, use `down` without `--volumes`:

```sh
docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  down --remove-orphans
```

The external telemetry network and Family Jobs Board resources remain
untouched. Do not add `--volumes`: that is a destructive telemetry reset.

## Capacity checks

Check the actual mounted capacities and usage from the pinned utility image:

```sh
docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-prometheus-data:/data:ro \
  busybox:1.37.0-uclibc df -Pm /data

docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-loki-data:/data:ro \
  busybox:1.37.0-uclibc df -Pm /data
```

The `1M-blocks` column must be no greater than 2048 for Prometheus and 4096
for Loki. Grafana's volume-use panels do not replace checking the host
filesystem and its alerts because filesystem metadata and unrelated overhead
sit outside Docker's per-volume byte totals.

## Upgrade

Before upgrading, take a stopped backup. Then check out the reviewed repository
revision, rerun validation, pull/build the newly pinned images, and run the
production `up --detach --wait` command. Inspect `ps --all` and the health
dashboard before deleting any old backup. Never replace a pinned tag with
`latest`.

If an upgrade fails, check out the last known-good configuration and pinned
versions, rebuild, and rerun `up`. Telemetry schemas are operational data, not
business records; do not attempt a blind downgrade over a data directory when
an upstream release says its storage migration is irreversible. Restore the
pre-upgrade backup to new volumes instead.

## Backup

Stop the stack first so the four volume archives are mutually consistent. The
following example writes to a root-only directory; choose a new dated path for
each backup:

```sh
backup_dir=/srv/backups/family-jobs-board-observability/2026-10-07T120000Z
sudo install -d -m 700 "$backup_dir"

docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  stop

docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-grafana-data:/source:ro \
  -v "$backup_dir":/backup \
  busybox:1.37.0-uclibc tar czf /backup/grafana-data.tgz -C /source .
docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-prometheus-data:/source:ro \
  -v "$backup_dir":/backup \
  busybox:1.37.0-uclibc tar czf /backup/prometheus-data.tgz -C /source .
docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-loki-data:/source:ro \
  -v "$backup_dir":/backup \
  --cap-add DAC_READ_SEARCH \
  busybox:1.37.0-uclibc tar czf /backup/loki-data.tgz -C /source .
docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-alloy-data:/source:ro \
  -v "$backup_dir":/backup \
  --cap-add DAC_READ_SEARCH \
  busybox:1.37.0-uclibc tar czf /backup/alloy-data.tgz -C /source .

sudo sh -c 'cd -- "$1" && sha256sum -- *.tgz > SHA256SUMS' sh "$backup_dir"
```

Restart with the production `up --detach --wait` command. Store the Grafana
operator-password file in the server's existing encrypted secret backup, not
inside these telemetry archives.

## Restore without overwriting the source volumes

Keep the original volumes intact until the restored stack has been verified.
Provision two new capped filesystems, choose a new resource prefix and project
name, update `PROMETHEUS_DATA_DIR` and `LOKI_DATA_DIR` to those empty mounts,
and run `docker compose create` with the bounded-storage overlay. This creates
empty volumes without starting services. Extract each archive into its matching
new volume as root so numeric ownership is preserved, for example:

```sh
restore_prefix=family-jobs-board-observability-restore
restore_project=family-jobs-board-observability-restore
backup_dir=/srv/backups/family-jobs-board-observability/2026-10-07T120000Z

sudo sh -c 'cd -- "$1" && sha256sum --check SHA256SUMS' sh "$backup_dir"

OBSERVABILITY_RESOURCE_PREFIX="$restore_prefix" \
OBSERVABILITY_PROJECT_NAME="$restore_project" \
docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  create

docker run --rm --network none \
  -v "$restore_prefix-grafana-data":/restore \
  -v "$backup_dir":/backup:ro \
  busybox:1.37.0-uclibc tar xzf /backup/grafana-data.tgz -C /restore
docker run --rm --network none \
  -v "$restore_prefix-prometheus-data":/restore \
  -v "$backup_dir":/backup:ro \
  busybox:1.37.0-uclibc tar xzf /backup/prometheus-data.tgz -C /restore
docker run --rm --network none \
  -v "$restore_prefix-loki-data":/restore \
  -v "$backup_dir":/backup:ro \
  busybox:1.37.0-uclibc tar xzf /backup/loki-data.tgz -C /restore
docker run --rm --network none \
  -v "$restore_prefix-alloy-data":/restore \
  -v "$backup_dir":/backup:ro \
  busybox:1.37.0-uclibc tar xzf /backup/alloy-data.tgz -C /restore
```

Run the restored stack with the same two prefix variables and the production
`up --detach --wait` command. Verify health, capacity, data-source queries, and
the dashboard before retiring the old project. Because both stacks use the
same stable Alloy DNS name on the telemetry network, run only one collector at
a time; stop the old project before starting the restored one.
