# Serendipity observability deployment

This runbook is the deployment record for issue #131. It layers the existing
observability stack onto Serendipity without publishing Grafana or a telemetry
backend directly. Caddy remains the only HTTP/HTTPS listener on the LAN.

## Deployed paths and resources

| Resource | Production value |
| --- | --- |
| Observability source | `/home/codex/apps/family-jobs-board-observability/source` |
| Environment and operator secret | `/home/codex/apps/family-jobs-board-observability/config` |
| Prometheus filesystem image | `/home/codex/apps/family-jobs-board-observability/storage/prometheus.ext4` |
| Loki filesystem image | `/home/codex/apps/family-jobs-board-observability/storage/loki.ext4` |
| Prometheus mount | `/home/codex/apps/family-jobs-board-observability/data/prometheus` |
| Loki mount | `/home/codex/apps/family-jobs-board-observability/data/loki` |
| Compose project | `family-jobs-board-observability` |
| Private telemetry network | `family-jobs-board-telemetry` |
| Private monitor ingress | `family-jobs-board-monitor-ingress` |
| LAN address | `https://monitor.home.arpa` |

The telemetry network contains only the API and Alloy. The monitor ingress
contains only the Family Jobs Board Caddy proxy and Grafana. Grafana keeps its
query-only network and does not join the application default network.

## Storage provisioning

Serendipity uses two fully allocated ext4 filesystem images because its root
logical volume has ample capacity but no pre-existing quota-enabled child
filesystem. The images are mounted by `/etc/fstab` with
`loop,nofail,nodev,nosuid,noexec`. Their fixed filesystem sizes are the hard
capacity boundary required by `compose.storage-bounds.yaml`.

Create each image only when the path does not already exist:

```sh
install -d -m 700 /home/codex/apps/family-jobs-board-observability/storage
fallocate -l 2048M /home/codex/apps/family-jobs-board-observability/storage/prometheus.ext4
fallocate -l 4096M /home/codex/apps/family-jobs-board-observability/storage/loki.ext4
chmod 600 /home/codex/apps/family-jobs-board-observability/storage/*.ext4
mkfs.ext4 -F -m 0 /home/codex/apps/family-jobs-board-observability/storage/prometheus.ext4
mkfs.ext4 -F -m 0 /home/codex/apps/family-jobs-board-observability/storage/loki.ext4
```

Back up `/etc/fstab`, add only these entries, validate with `findmnt --verify`,
then mount the two paths:

```fstab
/home/codex/apps/family-jobs-board-observability/storage/prometheus.ext4 /home/codex/apps/family-jobs-board-observability/data/prometheus ext4 loop,nofail,nodev,nosuid,noexec 0 2
/home/codex/apps/family-jobs-board-observability/storage/loki.ext4 /home/codex/apps/family-jobs-board-observability/data/loki ext4 loop,nofail,nodev,nosuid,noexec 0 2
```

`df -Pm` must report no more than 2048 MiB and 4096 MiB respectively.

## Private networks and application adapter

Create or validate both internal bridges before applying either Compose model:

```sh
./scripts/ensure-observability-network.sh
./scripts/ensure-monitor-network.sh
```

The trusted application deployment command includes `compose.telemetry.yaml`
and `compose.monitor.yaml`. Enabling telemetry on an existing API requires one
intentional API replacement because Docker cannot add exporter environment
variables to a running container. Establish a new before-state after that
one-time prerequisite; subsequent observability stop/start validation must not
replace any application container or volume.

## Start and expose the stack

Serendipity currently uses Canonical's confined Docker Snap. On this host,
Docker rejects a container's first `exec` when `no-new-privileges` is enabled,
even with both the container AppArmor and seccomp profiles disabled. Confirm
that host limitation before using the compatibility overlay:

```sh
docker run --rm \
  --network none \
  --read-only \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  busybox:1.37.0-uclibc /bin/sh -c true
```

The expected Serendipity result is exit `255` with
`exec /bin/sh: operation not permitted`; omitting only
`--security-opt no-new-privileges:true` succeeds. The
`compose.snap-docker-compat.yaml` overlay therefore removes only that flag.
Read-only filesystems, all dropped capabilities, non-root service users,
resource limits, and network isolation remain active. Do not use this overlay
on a Docker installation where the probe succeeds. Migrating Serendipity from
the Docker Snap to Docker Engine packages removes this residual exception.

Render and validate the exact production model before starting it:

```sh
docker compose \
  --env-file /home/codex/apps/family-jobs-board-observability/config/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  -f observability/compose.ingress.yaml \
  -f observability/compose.snap-docker-compat.yaml \
  config --quiet

docker compose \
  --env-file /home/codex/apps/family-jobs-board-observability/config/observability.env \
  -f observability/compose.yaml \
  -f observability/compose.storage-bounds.yaml \
  -f observability/compose.ingress.yaml \
  -f observability/compose.snap-docker-compat.yaml \
  up --detach --wait
```

The Caddyfile and Corefile are file bind mounts. Do not replace either host path
with `rename(2)` or `mv`: a running container remains pinned to the old inode
and its reload mechanism cannot see the replacement. For Caddy, validate and
load the reviewed Caddyfile from standard input so the existing proxy is not
recreated:

```sh
docker exec -i family-jobs-board-proxy-1 \
  caddy validate --config /dev/stdin --adapter caddyfile < deploy/Caddyfile
docker exec -i family-jobs-board-proxy-1 \
  caddy reload --config /dev/stdin --adapter caddyfile < deploy/Caddyfile
```

Keep the reviewed Caddyfile at the bind source for the next container start.
For CoreDNS, copy only `192.168.0.100 monitor.home.arpa` into the existing
Corefile **in place**, preserving the inode; the `reload` plugin then applies
it without a container restart. If the bind source has already been replaced,
validate the new file and perform one controlled CoreDNS recreation rather
than weakening or remounting the read-only bind.

## Verification

- `docker compose ps --all` shows every long-running observability service
  healthy and both storage one-shots exited zero.
- No observability service publishes a host port. Caddy remains the only
  process listening on LAN ports 80 and 443.
- The anonymous Grafana session is a Viewer, cannot save or administer, and
  can access the three provisioned dashboards.
- Prometheus reports 30-day and 1600 MiB retention; Loki reports 720 hours.
- Prometheus targets, application telemetry, container state, deployment image,
  and host metrics are fresh.
- Application and CoreDNS container identities, restart counts, OOM state, and
  the PostgreSQL volume are compared with the captured baseline.
- Stop and start only the observability project while continuously checking
  Family Jobs Board health; no application container may be replaced.

## Rollback

1. Restore only the backed-up CoreDNS and Caddy files, validate them, and reload
   those services.
2. Restore the trusted application deployment command and its Compose files if
   their integration change caused the failure.
3. Stop the observability project with `down --remove-orphans` using all four
   production Compose files. Never pass `--volumes`.
4. Preserve the four telemetry volumes and both filesystem images unless Adam
   explicitly authorizes deletion.
5. If storage itself must be rolled back, unmount only the two observability
   mount points and restore the backed-up `/etc/fstab`.

Never use Docker prune or stop an unrelated Compose project during rollback.

## Deployment record

Deployment completed on 2026-10-22 (Africa/Johannesburg).

- Application image revision:
  `42832ce726b4a98f3de5ed4caf56af253216b50d`.
- Before-state and rollback bundle:
  `/home/codex/backups/family-jobs-board-observability/20261022T170523Z`.
- Runtime: Canonical Docker Snap 29.8.0, Compose 5.5.1. The documented
  `no-new-privileges` probe failed with exit 255; the compatibility overlay was
  therefore required. All remaining hardening checks passed.
- Storage: Prometheus `/dev/loop3` reported 1946 MiB total; Loki `/dev/loop49`
  reported 3952 MiB total. Both were 1% used at verification.
- The one planned API replacement changed only the API container from
  `7c0eb9bf7802` to `2d2a0ae04fac`; database `c28eb95770d5`, web
  `ebca56a50195`, and proxy `68f8da82cd93` remained unchanged with zero
  restarts and no OOM events.
- The first Corefile installation replaced the bind-source inode. CoreDNS was
  therefore recreated once, from `d358bde40cac` to `b6d89c1f0957`, to mount
  the already validated file. Both DNS records then resolved to
  `192.168.0.100`; all application containers remained unchanged.
- All 15 Prometheus targets were up, application telemetry was fresh, the
  current aggregate was zero runtime errors and one warning, Prometheus
  reported `30d or 1GiB576MiB` retention, and Loki was configured for 720
  hours.
- Anonymous Grafana access reported Viewer semantics, all three provisioned
  dashboards were readable, anonymous administration returned HTTP 403, and
  no observability container published a host port.
- A full observability stop/start produced no failed Family Jobs Board health
  probes and did not replace any application container, CoreDNS container, or
  the PostgreSQL volume.
- This Mac resolved `monitor.home.arpa` through its normal resolver and trusted
  `https://monitor.home.arpa/api/health` without a certificate override.
