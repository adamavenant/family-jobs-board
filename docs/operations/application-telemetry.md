# Application telemetry contract

## Purpose and boundary

The API emits privacy-safe operational metrics and structured events for the
Family Jobs Board observability stack. The contract is backend-neutral: metrics
and safe events use OpenTelemetry and leave the process over private OTLP. Safe
events are also present in the structured console stream for local operations.

Telemetry is an API adapter concern. The Domain and Application projects have
no OpenTelemetry, Grafana, Prometheus, Loki, Docker, or Kubernetes references.
There is no public `/metrics` endpoint and telemetry is not part of liveness or
readiness.

The anonymous monitoring data is deliberately separate from the existing
`IdentityAudit` stream. Identity audit records retain actor, target, session,
and correlation data required by the identity specification and **must go only
to a protected audit sink**. The OTLP logging provider exports only the exact
safe category described below; it never exports `IdentityAudit` or general
application/framework records. The anonymous collector does not ingest Docker
stdout.

## Optional OTLP export

Metric export is disabled when no endpoint is configured. Enable it with the
standard OpenTelemetry variables:

| Variable | Value used by the Compose adapter |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://alloy:4317` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` |
| `OTEL_SERVICE_NAME` | `family-jobs-board-api` |
| `OTEL_SERVICE_VERSION` | immutable deployed image tag/SHA |
| `Telemetry__Environment` | stable environment name, normally `production` |

`Telemetry__Environment` is normalized to one of the shared finite values
`development`, `local`, `production`, `staging`, `test`, or `testing`. Any
other configured value falls back to a recognized host environment, then to
`production`. Do not place a household, host, cluster, or deployment-specific
identifier in this field.

`OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` / `_PROTOCOL` and
`OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` / `_PROTOCOL` may override each signal.
Supported protocols are `grpc` and `http/protobuf`. For a common
`http/protobuf` endpoint, the API appends `/v1/metrics` and `/v1/logs` as the
OpenTelemetry specification requires. Signal-specific HTTP endpoints are used
verbatim and therefore must include their complete receiver path. Invalid
endpoint/protocol configuration disables both exporters and produces one
sanitized warning; the configured value is never logged.

The metric reader exports every 30 seconds on a background worker and bounds
each attempt to five seconds. Safe logs use a bounded 2,048-record background
queue, batches of at most 512, a five-second schedule, and a five-second export
timeout. Full queues drop telemetry instead of applying request backpressure.
Connection refusal, timeout, or a missing collector does not block requests,
fail startup, or change `/health/live` or `/health/ready`.

The base Compose files have no telemetry-network dependency. To opt in, first
start the independently managed observability project that owns the external
telemetry network, then include the adapter. Both projects use
`TELEMETRY_NETWORK_NAME`, which defaults to `family-jobs-board-telemetry`; when
overriding it, supply the same value to both Compose commands.

```sh
docker compose \
  -f compose.yaml \
  -f compose.production.yaml \
  -f compose.telemetry.yaml \
  up -d
```

The adapter adds only the API to the private network. It publishes no port and
does not make an OTLP receiver or diagnostic endpoint reachable through the
Family Jobs Board proxy. Starting the application without this override keeps
it independent of the collector and its network.

The API writes one structured completion event per request, including health
checks. Its Docker `json-file` output is therefore capped at three 10 MB files
by the base Compose model. Keep an equivalent bounded log policy when deploying
the API outside that model.

## Metric contract

All metric names, types, units, and exported label keys are fixed below. Metric
views drop every non-allowlisted HTTP attribute and exemplars are disabled, so
trace IDs and filtered attributes cannot travel as exemplar metadata.

| Metric | Type/unit | Exported labels | Meaning |
| --- | --- | --- | --- |
| `http.server.request.duration` | Histogram, seconds | `http.request.method`, `http.route`, `http.response.status_code` | Built-in ASP.NET Core server duration. Its histogram sample count is the HTTP request count; its buckets support request rate and p50/p95 panels. |
| `family_jobs_board.application.runtime_error.count` | Counter, events | `event.severity` (`error` or `critical`) | Aggregate count of API/framework Error/Critical records, including sanitized unhandled failures. Record bodies and categories are not metric labels. |
| `family_jobs_board.application.warning.count` | Counter, events | none | Aggregate count of API/framework Warning records. Record bodies and categories are not metric labels. |
| `aspnetcore.diagnostics.exceptions` | Counter, exceptions | `aspnetcore.diagnostics.exception.result` | Built-in ASP.NET Core count of exceptions caught by exception middleware. Application failures are handled into safe 500 responses, so the result is `handled`. |
| `family_jobs_board.authentication.rejection.count` | Counter, rejections | `http.request.method`, `http.route`, `http.response.status_code` | Expected 401/403 outcomes and sign-in 429 throttling, separate from runtime errors. |
| `family_jobs_board.process.start.time` | Gauge, Unix seconds | none | API process start time; a value change identifies a restart or replacement. |

The severity counters deliberately exclude `IdentityAudit`: its sign-in
rejection warning is an expected authentication outcome and its structured
fields contain protected identifiers. They also exclude the framework exception
handler category and Kestrel event 13 (`ApplicationError`) because those local
diagnostic records describe failures already counted by the sanitized event.
General log records are never OTLP-exported; only their aggregate severity
changes the counter.

The exact .NET 10 `System.Runtime` export contract is below. `Sum` and
`non-monotonic sum` are the OpenTelemetry SDK aggregations produced by the
built-in instruments. A collector may translate non-monotonic sums to gauges.

| Metric | Type/unit | Exported labels |
| --- | --- | --- |
| `dotnet.assembly.count` | Non-monotonic sum, `{assembly}` | none |
| `dotnet.gc.collections` | Sum, `{collection}` | `gc.heap.generation` (`gen0`, `gen1`, `gen2`, `loh`, `poh`) |
| `dotnet.gc.heap.total_allocated` | Sum, `By` | none |
| `dotnet.gc.last_collection.heap.fragmentation.size` | Non-monotonic sum, `By` | `gc.heap.generation` (`gen0`, `gen1`, `gen2`, `loh`, `poh`) |
| `dotnet.gc.last_collection.heap.size` | Non-monotonic sum, `By` | `gc.heap.generation` (`gen0`, `gen1`, `gen2`, `loh`, `poh`) |
| `dotnet.gc.last_collection.memory.committed_size` | Non-monotonic sum, `By` | none |
| `dotnet.gc.pause.time` | Sum, `s` | none |
| `dotnet.jit.compilation.time` | Sum, `s` | none |
| `dotnet.jit.compiled_il.size` | Sum, `By` | none |
| `dotnet.jit.compiled_methods` | Sum, `{method}` | none |
| `dotnet.monitor.lock_contentions` | Sum, `{contention}` | none |
| `dotnet.process.cpu.count` | Non-monotonic sum, `{cpu}` | none |
| `dotnet.process.cpu.time` | Sum, `s` | `cpu.mode` (`user`, `system`) |
| `dotnet.process.memory.working_set` | Non-monotonic sum, `By` | none |
| `dotnet.thread_pool.work_item.count` | Sum, `{work_item}` | none |
| `dotnet.timer.count` | Non-monotonic sum, `{timer}` | none |

Unknown/future `System.Runtime` instruments are dropped until reviewed. The
`dotnet.exceptions` instrument is intentionally dropped because it counts
first-chance exceptions, including exceptions the application handles, and is
not the runtime-error definition. The .NET 10
`dotnet.thread_pool.queue.length` and `dotnet.thread_pool.thread.count`
instruments are also dropped because that runtime incorrectly exports their
decreasing current values as monotonic sums. Re-evaluate them after the .NET 11
runtime fix rather than graphing them as counters or applying `rate`/`increase`.

### HTTP cardinality and traffic semantics

ASP.NET Core supplies the matched route template, not the raw URL. For example,
all `/api/jobs/<guid>/complete` requests aggregate under
`/api/jobs/{id:guid}/complete`. Unmatched URLs have no `http.route` label and
therefore aggregate into one method/status series. Query strings are never a
label. The metric view also drops scheme, network protocol, original unknown
methods, and error type. Unknown HTTP methods use the framework's bounded
`_OTHER` value.

The API health check in Compose calls `/health/ready` every three seconds, and
blackbox probes add more health traffic. Keep those measurements for service
availability, but exclude the stable route templates `/health/live` and
`/health/ready` from household-traffic panels. This is route-template
filtering, never raw-path or query-string matching.

### Exact-once failure semantics

One controlled unhandled request produces exactly:

1. one `runtime_error.count{event.severity="error"}` increment;
2. one `aspnetcore.diagnostics.exceptions{aspnetcore.diagnostics.exception.result="handled"}` increment;
3. one `http.server.request.duration` sample with status `500`; and
4. one sanitized Error event plus the normal request-completed event.

These are intentional views of the same incident, not independent errors.
Dashboard panels must not add them together. Warning events and 4xx client or
authentication outcomes never increment runtime errors.

Counters and histogram state reset when the process restarts. Prometheus stores
the time series across that reset and uses reset-aware `rate`/`increase`
functions. The process-start gauge makes restarts visible without a container
name or ID. The API emits `service.version` to the collector so it can validate
the bounded deployment value, but the anonymous collector discards it before
Prometheus or Loki storage to prevent unbounded deployment labels.

## Safe structured-event contract

Console output uses the .NET JSON formatter with UTC timestamps and retains
normal structured scopes and activity correlation for protected operational
and identity-audit diagnostics. Safe event attributes are written as structured
log state, and the safe writer clears its ambient activity while emitting so
its console and OTLP records do not inherit trace/span IDs. A separate
OpenTelemetry logging provider disables scopes and uses an exact category
filter, so only records satisfying both of these conditions leave the API over
OTLP and may enter anonymous Loki:

- logger category exactly `FamilyJobsBoard.Telemetry`; and
- `telemetry.schema` exactly `family-jobs-board.event.v1`.

The application-emitted safe-state allowlist is:

- `telemetry.schema`
- `service.name`
- `service.version`
- `deployment.environment.name`
- `event.name`
- `http.request.method`
- `http.route`
- `http.response.status_code`
- `http.response.status_class`
- `duration_ms`
- `reason`

The JSON console record includes its numeric .NET `EventId`; `event.name` is the
stable named identity on both transports. OpenTelemetry .NET 1.19 does not put
the .NET `EventId` on the stable OTLP wire, so Loki correlation must use
`event.name`, not an experimental exporter switch.

The collector replaces the body with `event.name`. It retains the normal safe
OTLP envelope (timestamps, severity, instrumentation scope) and these resource
attributes:

- `service.name`
- `deployment.environment.name`

The API also sends its bounded `service.version` resource value. The collector
validates that value and then discards it before anonymous storage.

The API constructs its resource from an empty builder. `service.name`,
`service.version`, and `deployment.environment.name` are its complete emitted
resource allowlist for both metrics and events;
`OTEL_RESOURCE_ATTRIBUTES` and automatic/default resource detectors cannot add
host, container, instance, household, or other attributes to exported signals.

It retains only these application-provided log attributes:

- `telemetry.schema`
- `event.name`
- `http.request.method`
- `http.route`
- `http.response.status_code`
- `duration_ms`

The collector drops `http.response.status_class`, `reason`, the original
body/message, exception fields, trace/span fields, and every other attribute.

Current stable event names are:

- `application.started`
- `http.request.completed`
- `authentication.rejected`
- `http.request.unhandled_failure`
- `telemetry.configuration.ignored`

`application.started` is registered on the host's `ApplicationStarted`
lifetime signal. It is emitted only after every hosted service has started
successfully, never merely because the application pipeline was built.

`reason` is emitted only on `telemetry.configuration.ignored` and is limited to
`invalid_endpoint` or `invalid_protocol`; it is intentionally not retained in
anonymous Loki. Any other event name, reason, or attribute is outside this
contract and must be dropped by the collector.

Request correlation in the anonymous stream means the bounded method, route
template, status, version, and time window. It intentionally does **not** use a
trace ID, span ID, request ID, session ID, member/job ID, container ID, or
client IP. The event writer clears the ambient activity while emitting each
safe record, the OTLP logger excludes scopes and formatted state, distributed
traces are out of scope, and metrics exemplars are disabled. The collector also
clears trace/span fields as defense in depth.

Unhandled exceptions are converted to a fixed RFC Problem Details response.
The exception object, type, message, stack, request body, raw URL, query string,
headers, cookies, and payload are not passed to the safe event logger or OTLP.
The standard exception-handler diagnostic remains in the protected local console
stream with its exception and stack trace so an operator can investigate a 500;
the anonymous collector must never ingest general container stdout.

## Privacy and review checklist

Neither metric labels nor safe events may contain:

- first names, surnames, nicknames, job names/descriptions, adjustment reasons,
  or any other household content;
- member, job, request, session, trace, span, container, or other unique IDs;
- PINs, setup/refresh/access tokens, cookies, authorization headers, request or
  response bodies, client IPs, connection strings, raw paths, or query strings.

New safe metric labels and event attributes require explicit allowlist review
for privacy and cardinality. Never broaden OTLP log export beyond the exact
`FamilyJobsBoard.Telemetry` category: doing so would mix the protected identity
audit stream into anonymously queryable telemetry.
