# Observability stack operations

This runbook operates the separate Grafana/Prometheus/Loki/Alloy/Blackbox
Compose project. It does not deploy the Family Jobs Board application, change
DNS or reverse-proxy configuration, or expose Grafana to the LAN.

## Prepare configuration

Install Docker Engine with the Compose plugin. Copy the example outside the
checkout and create a strong Grafana operator password as a separate mode-0600
file:

```sh
sudo install -d -m 700 /etc/family-jobs-board-observability
sudo cp observability/environment.example \
  /etc/family-jobs-board-observability/observability.env
sudo sh -c 'umask 077; openssl rand -base64 48 > /etc/family-jobs-board-observability/grafana-admin-password'
```

The password is for explicit operator sign-in only. Anonymous users are
Viewers and cannot edit dashboards or administer Grafana. Never commit either
file.

Run the network preflight before either Compose project uses telemetry:

```sh
./scripts/ensure-observability-network.sh
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

Pull the directly used images and rebuild the three health-check wrapper
images from their pinned bases:

```sh
docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  pull grafana prometheus storage-init

docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  build --pull
```

Production startup must include the bounded-storage overlay:

```sh
./scripts/ensure-observability-network.sh

docker compose \
  --env-file /etc/family-jobs-board-observability/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  up --detach --wait
```

The one-shot storage preflight and ownership initializer must exit successfully;
Grafana, Prometheus, Loki, Alloy, and Blackbox must report healthy:

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
configuration change. To remove containers and the project-private backend
network while preserving all data, use `down` without `--volumes`:

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
for Loki. Grafana's health dashboard shows Prometheus block and Loki WAL use,
but those panels do not replace checking the host filesystem and its alerts.

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
  busybox:1.37.0-uclibc tar czf /backup/loki-data.tgz -C /source .
docker run --rm --network none --read-only --cap-drop ALL \
  -v family-jobs-board-observability-alloy-data:/source:ro \
  -v "$backup_dir":/backup \
  busybox:1.37.0-uclibc tar czf /backup/alloy-data.tgz -C /source .

sudo sha256sum "$backup_dir"/*.tgz | sudo tee "$backup_dir/SHA256SUMS"
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

sudo sha256sum --check "$backup_dir/SHA256SUMS"

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
