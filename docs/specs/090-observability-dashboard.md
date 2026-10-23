# Family Jobs Board observability dashboard

## Status

Accepted implementation contract. Issue #127 delivers the independently
deployable storage and visualisation stack, issue #128 adds privacy-safe
application telemetry, issue #129 adds bounded host/service/container health,
and issue #130 provisions the operator-facing application and server
dashboards. Reverse-proxy/DNS integration and live Serendipity deployment are
separate increments.

## Outcome and user value

An operator can run a self-hosted Grafana OSS dashboard beside Family Jobs
Board without coupling the two Compose lifecycles. The first dashboard proves
that the telemetry path is fresh, its private services are healthy, and its
storage is operating inside a bounded envelope. The same OpenTelemetry
contract can later feed a Kubernetes deployment rather than binding the
application to Docker-specific collection.

Grafana OSS is the free, self-managed open-source edition. Running it here does
not require Grafana Cloud or an Enterprise subscription, although the
household remains responsible for hosting, storage, upgrades, and backups.

## Actors and access

| Capability | LAN visitor | Operator |
| --- | --- | --- |
| View provisioned dashboards | Anonymous `Viewer` | Yes |
| Query provisioned Prometheus/Loki data sources | Yes | Yes |
| Edit dashboards or data sources | No | Yes, after explicit sign-in |
| Administer Grafana or the telemetry services | No | Yes, out of band |

Anonymous Viewers can submit their own PromQL and LogQL through Grafana's data
source proxy even when Explore and dashboard editing are disabled. The privacy
boundary is therefore the collector allowlist, not the dashboard. Anything
stored in Prometheus or Loki must be safe for every LAN visitor to query.
Grafana is also isolated from both storage services by an internal query
network. Its provisioned data sources can reach only a deny-by-default gateway
that allows reviewed read/query paths and methods. Ingest, administration, and
all other backend paths are absent, so an anonymous data-source proxy request
cannot bypass Alloy and write directly to Prometheus or Loki.

Issue #127 publishes no host ports. A later deployment issue may route only
Grafana through `monitor.home.arpa`. Anonymous access must be revisited
before any internet or AKS exposure; the LAN-only decision is not portable to
a public deployment.

## Topology and lifecycle

The observability project has its own project name, configuration, backend
network, named volumes, and containers. It does not share the application
project's lifecycle or volumes and has no PostgreSQL or privileged-container
access. Issue #129 adds narrowly scoped, read-only host mounts to Node Exporter
and a Docker socket bind only to the private, privacy-bounded container
exporter; neither Grafana nor the other telemetry services receive those
mounts.

The only cross-project resource is an external, internal Docker bridge named
`family-jobs-board-telemetry`. An operator creates and validates it before
starting either project. Both projects declare it external, so `docker compose
down` in either project cannot remove it. The application joins it only through
its optional telemetry override and remains independently startable when the
collector is absent. Export failure is fail-open for application requests.

```text
Family Jobs Board API -- private OTLP --> Alloy --> Prometheus
                                          |
                                          +------> Loki

LAN ingress (later) --> Grafana --> query-only gateway --> Prometheus (read)
                                                    |
                                                    +----> Loki (read)
```

Prometheus, Loki, Alloy, Blackbox Exporter, the query gateway, and direct
Grafana ports remain unpublished. A loopback-only Grafana port exists solely
in the automated test override and must not be used for deployment.

### Serendipity and service health

Issue #129 adds a separate **Server and service health** dashboard. Its
platform-neutral workload label is the fixed Compose service name, which maps
cleanly to a future Kubernetes workload label without changing panel meaning.
It includes Serendipity CPU/load, memory, uptime, and root-filesystem capacity;
web/API liveness/readiness probes; and the four Family Jobs Board runtime
containers' presence, running/state, Docker health, restart count, OOM result,
start/age, image tag, and immutable Docker image content ID.

The container-state path is deliberately narrower than a general Docker
exporter. One unprivileged sanitizer is the only service with a read-only
socket bind. Its Docker client permits only container-list, container-inspect,
and volume disk-usage reads, applies exact Compose labels plus the fixed `api`,
`db`, `proxy`, and `web` allowlist, rechecks inspected labels, and retains only
the exact observability Prometheus/Loki volume byte totals from disk usage. It
refreshes the host-wide Docker disk-usage call at most every five minutes,
serves cached values between refreshes, and exports separate storage-collection
success/freshness so a failed refresh never looks current. It
exposes only sanitized metrics and fail-closed health over the private backend
network. No raw Docker API, unrelated container or volume inventory, names,
IDs, environment, commands, mounts, arbitrary labels, logs, host processes,
host connections, or host filesystem contents are exposed. Collector success
and last-success timestamps make stale data visible. The socket remains
root-equivalent despite its
read-only bind, so compromise of the sanitizer could bypass its policy; the
small surface and container hardening reduce but do not remove that risk.

## Application telemetry wire contract

The stable receiver addresses on the external telemetry network are:

- OTLP/gRPC common endpoint: `http://alloy:4317` (preferred for the .NET
  application);
- OTLP/HTTP common endpoint: `http://alloy:4318`; OTLP/HTTP clients append the
  standard `/v1/metrics` or `/v1/logs` signal path; and
- no trace signal is accepted or stored by this increment.

The application sets `OTEL_EXPORTER_OTLP_ENDPOINT=http://alloy:4317`,
`OTEL_EXPORTER_OTLP_PROTOCOL=grpc`, and
`OTEL_SERVICE_NAME=family-jobs-board-api` in its optional Compose override.
Signal-specific paths must not be included in the gRPC/common endpoint.

The collector accepts only the following resource attributes. Invalid
resource values cause the whole data point or event to be rejected rather than
being truncated into a colliding series:

- `service.name`, which must equal `family-jobs-board-api`;
- `service.version`, which must be 1-128 ASCII letters, digits, `.`, `_`, or
  `-`, beginning with a letter or digit (validated, then discarded before
  export so version values cannot create series); and
- `deployment.environment.name`, which must be one of `development`, `local`,
  `production`, `staging`, `test`, or `testing`.

All other resource and instrumentation-scope attributes are removed.

### Metrics

Counters and histograms use cumulative temporality. The request histogram's
count is the request count; its sum and buckets are in seconds. The allowlist
also enforces each metric's reviewed instrumentation scope and OTLP type.
Descriptions and units are replaced with the fixed contract values before
export so arbitrary metric metadata cannot become queryable. The allowlist is:

| Instrument | Kind | Allowed data-point attributes |
| --- | --- | --- |
| `http.server.request.duration` | Histogram, seconds | `http.request.method`, `http.route`, `http.response.status_code` |
| `aspnetcore.diagnostics.exceptions` | Counter | `aspnetcore.diagnostics.exception.result` (`handled`, `unhandled`, `skipped`, or `aborted`) |
| `family_jobs_board.application.runtime_error.count` | Counter | `event.severity` (`error` or `critical`) |
| `family_jobs_board.application.warning.count` | Counter | None |
| `family_jobs_board.authentication.rejection.count` | Counter | `http.request.method`, `http.route`, `http.response.status_code` |
| `family_jobs_board.process.start.time` | Gauge, Unix seconds | None |
| The 16 reviewed .NET 10 runtime instruments listed below | Runtime metrics | Exact bounded attributes described below |

HTTP methods use the bounded semantic-convention set `CONNECT`, `DELETE`,
`GET`, `HEAD`, `OPTIONS`, `PATCH`, `POST`, `PUT`, `TRACE`, or `_OTHER`.
Routes are restricted to the application's reviewed route-template allowlist;
raw paths, query strings, new/unreviewed templates, and malformed status codes
cause the entire point to be rejected. The request histogram must use the
application's exact bucket boundaries (`0.005`, `0.01`, `0.025`, `0.05`,
`0.1`, `0.25`, `0.5`, `1`, `2.5`, `5`, and `10` seconds), preventing hostile
payloads from creating arbitrary `le` labels.

The reviewed runtime names are `dotnet.assembly.count`,
`dotnet.gc.collections`, `dotnet.gc.heap.total_allocated`,
`dotnet.gc.last_collection.heap.fragmentation.size`,
`dotnet.gc.last_collection.heap.size`,
`dotnet.gc.last_collection.memory.committed_size`, `dotnet.gc.pause.time`,
`dotnet.jit.compilation.time`, `dotnet.jit.compiled_il.size`,
`dotnet.jit.compiled_methods`, `dotnet.monitor.lock_contentions`,
`dotnet.process.cpu.count`, `dotnet.process.cpu.time`,
`dotnet.process.memory.working_set`, `dotnet.thread_pool.work_item.count`, and
`dotnet.timer.count`. Future `dotnet.*` instruments are denied until reviewed.
Only `gc.heap.generation` (`gen0`, `gen1`, `gen2`, `loh`, or `poh`) is kept on
the three generation-split instruments `dotnet.gc.collections`,
`dotnet.gc.last_collection.heap.fragmentation.size`, and
`dotnet.gc.last_collection.heap.size`. Only `cpu.mode` (`user` or `system`) is
kept on `dotnet.process.cpu.time`. These dimensions prevent distinct runtime
points from collapsing into duplicate Prometheus series; all other runtime
attributes are removed.
`dotnet.exceptions` is explicitly rejected because exception type is
high-cardinality and may reveal implementation detail. Member,
household, job, request, session, connection, trace, span, client-IP, user-agent,
raw-URL, and free-text dimensions are never retained.

### Noteworthy events

The paired application-telemetry contract requires its OTLP logger to filter
to the exact `FamilyJobsBoard.Telemetry` category before export. The collector
does not rely on that upstream filter: it independently validates the
instrumentation scope and versioned schema before anonymous storage.

Loki accepts an OTLP log record only when all of these are true:

- instrumentation scope name is exactly `FamilyJobsBoard.Telemetry`;
- attribute `telemetry.schema` is exactly
  `family-jobs-board.event.v1`; and
- `event.name` is exactly one of `application.started`,
  `http.request.completed`, `authentication.rejected`,
  `http.request.unhandled_failure`, or
  `telemetry.configuration.ignored`.

The original message/body is replaced with `event.name`. Trace and span IDs
are zeroed, the instrumentation-scope version is cleared, and severity text is
canonicalised from the accepted event name. Only the schema, event name,
coarse route template, HTTP method,
HTTP status code, and numeric duration may remain as log attributes; all other
fields, including the application's optional configuration `reason`, are
dropped. `duration_ms` is accepted from zero through 600,000 milliseconds,
inclusive, matching the application's ten-minute clamp; larger values are
rejected. General API console output, Docker logs, IdentityAudit records, raw
exceptions, request/response bodies, tokens, cookies, PINs, connection strings,
and household content never enter Loki.

## Retention and bounded storage

Prometheus retains data for 30 days or until 1600 MiB of compacted blocks is
reached, whichever happens first. Production mounts its volume on a dedicated
filesystem of at most 2048 MiB, leaving more than 20% capacity for the WAL,
head block, and compaction overhead. The Prometheus size setting is a retention
trigger rather than a filesystem quota and may be exceeded temporarily; the
dedicated filesystem is the hard boundary.

Loki retains data for 720 hours using the Compactor. Chunks, WAL, index, and
Compactor deletion markers all persist under `/loki`. Production mounts that
volume on a dedicated filesystem of at most 4096 MiB because time retention
does not itself cap filesystem use. A startup preflight rejects production
paths backed by larger filesystems, preventing an accidentally unbounded
deployment.

Local named volumes are suitable for development and automated tests but are
not intrinsically quota-bounded. They must not be described as production
capacity enforcement. Every observability container also uses Docker's bounded
`local` logging driver independently of Loki retention.

## Provisioned health dashboard

The version-controlled `Telemetry stack health` dashboard shows:

- healthy and down scrape-target counts;
- aggregate and per-service sample freshness so stale green data is visible;
- private endpoint probe state;
- Prometheus volume use;
- privacy-safe Loki ingestion activity; and
- Loki volume use.

Prometheus and Loki data sources are provisioned read-only from files and point
only at the query gateway. Its exact path-and-method allowlist permits the
required PromQL/LogQL read APIs and denies ingest and administration. Query
timeouts, concurrency, series/sample, entry, and line limits bound the work an
anonymous Viewer can request.

## Provisioned operator dashboards

The default **Family Jobs Board overview** has a six-hour initial range, quick
ranges through the full 30-day retention period, and explicit `NO DATA`,
`STALE`, `HEALTHY`, `DEGRADED`, and `UNHEALTHY` presentation. Its summary and
panels keep the following signals separate:

- Error/Critical runtime events and unhandled ASP.NET Core exceptions;
- warnings, HTTP 4xx responses, and expected authentication rejections;
- request count/rate, status trends, p50/p95 latency, and reviewed route
  templates;
- aggregate application lifecycle, unhandled-failure, and telemetry
  configuration events; and
- endpoint readiness, bounded container state, restart/OOM history, process
  age, and sanitized image tag plus immutable image content ID.

Counter panels synthesize zero only while a fresh process-start gauge proves the
application telemetry path exists. Missing application telemetry therefore
renders `NO DATA`, never a reassuring zero. Headline totals use exact current
process counters and ignore samples timestamped before the current process
started, avoiding both restart carry-over and Prometheus range-boundary
extrapolation; the selected-range rate panels show when those events occurred.
Overall freshness
is the oldest required endpoint-probe, successful container-collection, or
application-telemetry sample; a failed required scrape is unhealthy rather
than fresh merely because Prometheus sampled its `up` value. Explicit outage
signals take precedence over missing telemetry, while an isolated volume-usage
refresh failure is degraded and leaves container health visible. A failed
migration is not directly exported: because API startup is migration-gated,
the dashboard presents its observable effect as missing/down API readiness
rather than claiming a migration-specific event.

The separate **Server and service health** dashboard retains Serendipity CPU,
load, memory, uptime, root byte capacity, root inode pressure, endpoint and
container health, and deployment identity. The privacy-bounded exporter also
uses Docker's volume disk-usage endpoint and retains only the exact
observability Prometheus and Loki volume sizes. Those panels therefore include
Prometheus WAL/head/compaction files and Loki chunks/WAL/index/Compactor state,
while production filesystem capacity remains enforced as described above.

Per-container CPU and memory are intentionally absent. The approved MVP uses
aggregate server capacity; adding cAdvisor or wider Docker statistics access
would expand the host-data and privilege boundary and requires a separate
decision.

## Acceptance scenarios

- Starting observability beside a healthy application leaves both projects
  healthy; stopping or recreating observability does not replace application
  containers or its database volume.
- Rendering the production Compose overlay fails unless Loki and Prometheus
  paths are explicit, and its runtime preflight fails if either underlying
  filesystem exceeds the documented budget.
- A hostile OTLP payload containing a private body, identifiers, arbitrary
  attributes, a general console category, or an unapproved metric/event cannot
  be found in Prometheus or Loki after collection.
- An anonymous browser is a Viewer, cannot save/administer the provisioned
  dashboard, receives a forbidden response from the administrative API, and
  cannot reach either storage service's OTLP ingest endpoint through Grafana's
  data-source proxy.
- The base Compose model publishes no ports. The verification-only override
  binds Grafana to `127.0.0.1`, never a LAN interface.
- A clean restart preserves telemetry volumes; ordinary shutdown and removal
  never delete them.

## Out of scope

Application instrumentation, host/node exporters, exact Docker container
inventory, Docker socket access, cAdvisor, product job-count gauges, final
application/capacity dashboards, alerts, live deployment, DNS/reverse-proxy
changes, and public-cloud identity are out of scope for issue #127.
