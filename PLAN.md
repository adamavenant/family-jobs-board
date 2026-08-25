# Family Jobs Board delivery plan

Status: proposed  
Last updated: 2026-08-24  
Scope: MVP described by `CONTEXT.md`, `docs/product-brief.md`, `docs/mvp-features.md`, and `docs/mvp-nonfunctional-reqs-and-architecture.md`

## 1. Outcome

Deliver the Family Jobs Board as a small, maintainable modular monolith with:

- a React single-page application;
- a .NET API;
- PostgreSQL persistence from the first runnable increment;
- all runtime components in separate containers;
- one repeatable Docker Compose startup path for development and the home server;
- a usable, tested product at the end of every phase; and
- issue-sized feature specifications that an implementation agent can execute without inventing product rules.

The first increment is a database-backed walking skeleton, not a throwaway mock. Each later phase extends the same schema, API, UI, tests, images, and Compose stack.

## 2. Current repository assessment

The current `main` branch contains product, domain, architecture, and agent guidance but no tracked application implementation. Ignored `bin/`, `obj/`, and `src/` build remnants and `stash@{0}` are not a supported baseline and must not be revived implicitly.

Before implementation begins:

1. Complete and accept `docs/adr/0002-modular-monolith.md`; it is currently empty.
2. Turn the relevant phase/spec into GitHub issues with acceptance criteria, as required by `docs/agents/developer-rules.md`.
3. Work from an issue branch while preserving the current uncommitted files.
4. Resolve the product decisions in section 12 when their owning phase is reached.

This plan assumes one household, one deployment, and one PostgreSQL database for the MVP. Multi-household tenancy and internet exposure are out of scope.

## 3. Delivery rules

1. **Compose stays green.** Every merged issue must leave the complete stack buildable and healthy with Docker Compose.
2. **PostgreSQL is used immediately.** Do not introduce an in-memory production repository and replace it later.
3. **Deliver vertical slices.** A feature issue includes its domain rule, migration, API, UI, authorization, automated tests, and documentation where those are applicable.
4. **Migrations are forward-only.** Every schema change is an EF Core migration checked into source control. Startup never silently creates or mutates schema.
5. **The ledger is the points source of truth.** Never update a child's points total directly.
6. **Dates are explicit.** Persist instants in UTC; calculate agenda dates and schedules in the configured household time zone.
7. **No destructive defaults.** Normal shutdown preserves the database volume. Resetting data is a separate, clearly named command.
8. **No credentials in Git or images.** Commit `.env.example`, not `.env`. Development defaults are local-only and production secrets are supplied by the deployment environment.
9. **Accessible by touch.** Test layouts at Kindle Fire and iPhone widths, with large targets, clear focus states, and no hover-only actions.
10. **Keep the MVP a monolith.** Use module boundaries in code, not independently deployed services.

## 4. Technical baseline

Pin exact application dependencies in lock files and central package management. Pin container images to supported major/minor lines and let an automated dependency update issue propose patch upgrades.

| Area | Baseline | Reason |
| --- | --- | --- |
| API | .NET 10 LTS, C# 14, ASP.NET Core Minimal APIs | Current active .NET LTS and a small HTTP surface |
| Persistence | EF Core 10 with Npgsql | Migrations, PostgreSQL integration, and conventional .NET support |
| Database | PostgreSQL 18 | Current supported PostgreSQL major |
| Web | React 19, TypeScript, Vite | SPA fits the local-network and responsive-tablet requirement without a server-rendering tier |
| Web build | Node.js 24 LTS; static assets served by nginx or another small unprivileged web image | Reproducible build image and simple runtime image |
| API contract | Built-in ASP.NET Core OpenAPI 3.1 document | Machine-readable contract without making a third-party documentation UI foundational |
| Tests | xUnit for .NET; Vitest and Testing Library for React; browser-level smoke tests in a later test profile | Fast inner loop plus real boundary coverage |

Do not float image tags such as `latest`. Record selected image tags/digests in the Phase 0 pull request and document the upgrade process.

## 5. Target repository layout

Phase 0 should create only the directories and files needed by its walking skeleton. Later feature directories are added by their owning issue.

```text
/
├── PLAN.md
├── README.md
├── CONTEXT.md
├── AGENTS.md
├── compose.yaml
├── .dockerignore
├── .env.example
├── Directory.Build.props
├── Directory.Packages.props
├── FamilyJobsBoard.sln
├── docs/
│   ├── adr/
│   │   ├── 0001-domain-model.md
│   │   └── 0002-modular-monolith.md
│   ├── agents/
│   ├── specs/
│   │   ├── README.md
│   │   ├── 000-platform-foundation.md
│   │   ├── 010-identity-and-users.md
│   │   ├── 020-jobs-and-agenda.md
│   │   ├── 030-completions-approvals-and-points.md
│   │   ├── 040-recurring-jobs-and-calendar.md
│   │   ├── 050-good-behaviours.md
│   │   ├── 060-redemptions-adjustments-and-audit.md
│   │   └── 070-production-readiness.md
│   └── operations/
│       ├── local-development.md
│       ├── backup-and-restore.md
│       └── home-server-deployment.md
├── src/
│   ├── backend/
│   │   ├── FamilyJobsBoard.Api/
│   │   │   ├── Features/
│   │   │   ├── Middleware/
│   │   │   └── Dockerfile
│   │   ├── FamilyJobsBoard.Application/
│   │   │   └── Features/
│   │   ├── FamilyJobsBoard.Domain/
│   │   └── FamilyJobsBoard.Infrastructure/
│   │       └── Persistence/
│   └── web/
│       ├── src/
│       │   ├── app/
│       │   ├── features/
│       │   ├── components/
│       │   └── api/
│       ├── Dockerfile
│       ├── nginx.conf
│       ├── package.json
│       └── package-lock.json
├── tests/
│   ├── FamilyJobsBoard.Domain.Tests/
│   ├── FamilyJobsBoard.Application.Tests/
│   ├── FamilyJobsBoard.IntegrationTests/
│   ├── FamilyJobsBoard.ArchitectureTests/
│   ├── web/
│   └── e2e/
└── scripts/
    ├── compose-up.sh
    ├── compose-down.sh
    ├── compose-test.sh
    └── print-urls.sh
```

Boundary intent:

- `Domain` owns aggregates, value objects, invariants, and domain events; it does not reference EF Core or ASP.NET Core.
- `Application` owns feature commands, queries, authorization-independent use cases, and ports.
- `Infrastructure` implements persistence, hashing, time, token, and other adapters.
- `Api` is the composition root and HTTP adapter. Endpoints are grouped by feature rather than by technical controller type.
- `web/src/features` mirrors user capabilities, not backend project layers.
- No module reads another module's EF tables directly. Shared behaviour is promoted deliberately, not placed in a miscellaneous `Helpers` directory.

The exact project split is subject to ADR 0002. Do not multiply assemblies unless the boundary is enforced by tests or useful to developers.

## 6. Docker Compose contract

The same root `compose.yaml` remains the canonical local and server topology throughout delivery.

### Services

| Service | Responsibility | Dependency condition |
| --- | --- | --- |
| `db` | PostgreSQL with a named persistent volume | Health check uses `pg_isready` |
| `migrate` | Runs checked-in EF Core migrations and exits | Starts after `db` is healthy; must complete successfully |
| `api` | ASP.NET Core API | Starts after `migrate` succeeds; readiness checks the database |
| `web` | Serves the compiled React SPA and reverse-proxies browser `/api` requests to the API container | Must expose its own health check |
| `app-links` | One-shot banner that prints all useful host URLs | Runs after `api` and `web` are healthy |

Add test-only services under a `test` profile rather than creating a second, drifting stack. Test services must not mount the host Docker socket.

The browser should call `/api` on the UI origin and let the `web` container proxy it to `api:8080`. This avoids environment-specific CORS configuration while retaining the directly published API port for development and diagnostics.

### Stable host URLs

Use overridable host ports with these defaults:

```text
UI:             http://localhost:3000
API base:       http://localhost:8080/api
OpenAPI JSON:   http://localhost:8080/openapi/v1.json
API liveness:   http://localhost:8080/health/live
API readiness:  http://localhost:8080/health/ready
```

The database is reachable by containers as `db:5432`. Publishing PostgreSQL to the host is opt-in through a Compose profile or port variable; the application does not require a host database port.

`app-links` and `scripts/print-urls.sh` must derive URLs from the same environment variables as Compose, so printed links cannot drift from the actual mappings.

### Developer commands

Attached startup, with the `app-links` banner in the normal logs:

```sh
docker compose up --build
```

Detached startup, health wait, and URL banner:

```sh
./scripts/compose-up.sh
```

Verification:

```sh
docker compose config --quiet
docker compose ps
./scripts/compose-test.sh
```

Non-destructive shutdown:

```sh
./scripts/compose-down.sh
```

The destructive database reset command, documented but never used by normal shutdown, is:

```sh
docker compose down --volumes
```

### Health semantics

- Liveness says the process can respond; it does not query PostgreSQL.
- Readiness says the API can execute a lightweight PostgreSQL query and is ready to serve traffic.
- The UI health check proves the static server is responding.
- `depends_on` uses `service_healthy` and `service_completed_successfully`, not timing sleeps.
- A phase is not complete if any required service is exited, restarting, or unhealthy after startup.

## 7. Core domain and persistence decisions

These rules prevent later phases from forcing destructive remodels.

### Identity and authorization

- A `User` is either `Adult` or `Child`; use an explicit role value rather than scattered `IsAdult` branches outside the domain.
- PINs are hashed with a slow, salted password-hashing algorithm. They are never encrypted, logged, returned, or stored as numbers.
- Adults have six-digit PINs and children have four-digit PINs.
- JWTs authorize API calls. To meet the ten-minute inactivity rule, use a short-lived access token plus activity-based renewal that cannot renew an already expired session. Specify renewal and revocation precisely in the identity spec.
- Rate-limit failed PIN attempts even on the local network; do not expose whether a PIN prefix or user is valid.

### Jobs and recurrence

- Separate the job definition/schedule from a dated `JobOccurrence` shown in the agenda.
- Once-off and recurring jobs both produce occurrences; the completion workflow operates on an occurrence, never on a recurrence template.
- Materialize recurring occurrences idempotently for a rolling horizon and on demand when navigating beyond it. Protect with a uniqueness constraint so restarts cannot duplicate jobs.
- Store the household time-zone identifier in configuration. Store scheduled local date/time explicitly and audit instants in UTC.
- Editing a recurring job must offer an explicit effective scope (`this occurrence` or `this and future`) once recurrence editing is implemented.

### Completion and approval

- An occurrence moves through explicit states such as `Scheduled`, `PendingApproval`, `Approved`, `Rejected`, and `Cancelled`; do not infer workflow solely from nullable timestamps.
- Submitting completion records the child and timestamp.
- Approving/rejecting records the adult, decision timestamp, optional reason, and approved point value.
- An approval and its point award occur in one database transaction.
- A repeated request cannot award points twice; enforce idempotency at both the endpoint and database level.

### Points

- `PointLedgerEntry` is append-only and contains child, signed delta, category, source reference, actor, occurred-at instant, and optional note.
- Current points are the sum of ledger entries. A cached balance may be added only with a documented consistency mechanism.
- Approval and good-behaviour entries are positive. Redemptions are negative. Manual adjustments may be either.
- Correct errors with compensating entries; do not edit or delete ledger history.
- Decide whether negative balances are allowed before implementing redemption.

### Audit and deletion

- Auditable records include created/updated actor and UTC timestamps.
- Soft deletion includes deleted-at and deleted-by. Queries exclude deleted records by default while audit views can include them.
- Establish the audit-event convention with the first business feature. Each phase records its own security-sensitive and points-changing actions; Phase 6 adds the searchable UI and verifies completeness rather than retrofitting all history.
- Never place PINs, JWTs, or other secrets in audit payloads.

## 8. Feature specification contract

Create each file under `docs/specs/` just before breaking its phase into implementation issues. A spec is accepted when its product rules and open questions are resolved; it is not a restatement of UI mock-ups.

Every feature spec must contain:

1. outcome and user value;
2. actors and authorization matrix;
3. in-scope and explicitly out-of-scope behaviour;
4. domain rules, state transitions, and failure cases;
5. proposed data changes and migration/rollback considerations;
6. HTTP operations, request/response examples, and error semantics;
7. UI states: loading, empty, populated, validation, error, and narrow-screen behaviour;
8. audit and security requirements;
9. observability and health impact;
10. acceptance criteria in Given/When/Then form;
11. automated test cases by layer;
12. Compose demonstration steps and URLs; and
13. unresolved decisions, owner, and deadline.

Each implementation issue should deliver one demonstrable slice from a spec. Avoid separate “backend”, “frontend”, and “database” issues that leave the feature unusable between merges.

## 9. Phased delivery

### Phase 0 — Containerized, database-backed walking skeleton

**Goal:** prove the architecture and developer loop before implementing product behaviour.

Deliver:

- accepted modular-monolith ADR and `docs/specs/000-platform-foundation.md`;
- solution/project scaffolding and dependency pinning;
- PostgreSQL, migration, API, web, and `app-links` Compose services;
- initial EF Core migration and a real database-backed readiness check;
- React shell that shows API/database readiness without exposing database details;
- same-origin `/api` proxying from the web container to the API container;
- OpenAPI JSON, structured console logging, correlation IDs, and centralized API problem responses;
- multi-stage, non-root production Dockerfiles compatible with Apple Silicon and the Linux target;
- unit, integration, and web smoke test containers under the `test` profile;
- scripts and README instructions for start, stop, test, URL display, and explicit data reset; and
- repository hygiene for generated output, `.DS_Store`, secrets, and local environment files; and
- CI that builds images, runs tests, starts Compose, checks health, and tears it down while preserving useful failure logs.

Exit gate:

- A clean checkout needs only Docker with Compose.
- `docker compose up --build` reaches a healthy steady state.
- The banner prints the five stable URLs from section 6.
- The UI loads and reports a successful call to the API.
- API readiness fails when PostgreSQL is unavailable and recovers when it returns.
- A migration has run; the API never calls `EnsureCreated`.
- Restarting the stack without deleting volumes preserves data/migration state.
- The test profile succeeds on both `arm64` development and `amd64` CI/target builds, or the documented multi-architecture build proves both platforms.

### Phase 1 — Household bootstrap, sign-in, and user administration

**Goal:** an adult can bootstrap the app, sign in, and manage family users; each user can select their identity and authenticate with the correct PIN rules.

Deliver from `docs/specs/010-identity-and-users.md`:

- first-run route that requires creation of the first adult;
- confirmed PIN setup during first-adult bootstrap;
- adult-authorized, one-time confirmed PIN setup for each subsequently created user, followed by the normal user chooser and PIN entry flow;
- hashed PIN storage, JWT issue/renewal/logout, inactivity expiry, and failed-attempt throttling;
- adult-only user list/create/edit/soft-delete/reset-PIN operations;
- child/adult authorization policies enforced by the API, not just hidden in the UI;
- duplicate-name display rule using surname only when needed; and
- bootstrap, authentication, authorization, expiry, and persistence tests.

Exit gate:

- An empty database shows bootstrap, not a broken login screen.
- The first user cannot be a child.
- A user whose PIN is not set cannot be claimed through the unauthenticated user chooser.
- Restarting Compose retains users and accepts their PINs.
- A child receives a forbidden response for adult endpoints.
- Incorrect PIN and expired-session paths return to user selection without leaking sensitive detail.
- The Compose commands and printed URLs remain unchanged.

### Phase 2 — Once-off jobs, daily agenda, and completion submission

**Goal:** an adult can assign dated jobs and a child can see and submit their work for approval.

Deliver from `docs/specs/020-jobs-and-agenda.md`:

- adult job create/edit/cancel/soft-delete for once-off jobs;
- assignee, name, description, whole-number points, date, optional time, and agenda period;
- daily agenda for the selected user, grouped into Morning, Arriving Home, Evening, and Unscheduled;
- previous/next-day navigation with an explicit household time zone;
- child completion submission and visible `PendingApproval` state;
- adult all-children daily view with child filter; and
- persistence, validation, authorization, responsive UI, and state-transition tests.

Exit gate:

- A job created in the UI appears for the correct child/date after an API and container restart.
- A child can submit only their own eligible occurrence and cannot approve it.
- Duplicate completion requests produce one submission.
- Empty, loading, validation, server-error, and offline/retry UI states are demonstrable.
- The full stack and test profile remain healthy through Compose.

### Phase 3 — Adult approval and the points ledger

**Goal:** complete the core jobs-to-reward loop with auditable, exactly-once point awards.

Deliver from `docs/specs/030-completions-approvals-and-points.md`:

- adult pending-approval queue;
- approve/reject with optional reason and approval-time point override;
- transactional, idempotent ledger award on approval;
- child running balance and points history showing source, amount, time, and approving adult;
- adult visibility of pending approvals on the home agenda; and
- concurrency tests for double approval and repeated requests.

Exit gate:

- Two concurrent approvals can create only one final decision and one ledger entry.
- Rejection awards no points and records its actor/reason.
- The balance exactly equals the signed ledger sum before and after restart.
- The UI demonstrates submit → approve/reject → updated balance without manual refresh where practical.
- Compose and all tests remain green.

This phase is the first complete core MVP loop and should be used for an in-home pilot before adding breadth.

### Phase 4 — Recurring jobs and adult calendar

**Goal:** make routine family jobs manageable without duplicating jobs by hand.

Deliver from `docs/specs/040-recurring-jobs-and-calendar.md`:

- daily, weekly, and monthly recurrence definitions;
- deterministic occurrence generation with uniqueness protection and a documented horizon;
- weekend-specific schedules or an explicit rule for expressing them;
- pause/end recurrence and scoped edits;
- adult day/week/month calendar views; and
- recurrence tests across month ends, leap years, daylight-saving boundaries, edits, cancellation, and restart.

Exit gate:

- Re-running generation or restarting containers creates no duplicates.
- Agenda and calendar read the same occurrences and workflow state.
- Editing a series never rewrites already approved history.
- Dates remain correct in the configured household time zone.
- Compose and all tests remain green.

### Phase 5 — Good behaviours

**Goal:** adults can award points immediately for predefined positive behaviours.

Deliver from `docs/specs/050-good-behaviours.md`:

- adult administration of good-behaviour types;
- seed strategy for examples such as Showing Kindness, Being Helpful, and Being Brave;
- log behaviour for a child with an editable point value;
- ledger entry created atomically with the behaviour log;
- child history integrated with job-earned points; and
- authorization, audit, idempotency, and persistence tests.

Exit gate:

- Logging once changes the ledger once, even after retry.
- Editing/deleting a behaviour type never changes historical ledger descriptions or amounts.
- Children can view but cannot create behaviour logs.
- Compose and all tests remain green.

### Phase 6 — Redemptions, manual adjustments, audit, and administration completeness

**Goal:** adults can manage the points lifecycle and inspect who changed important records.

Deliver from `docs/specs/060-redemptions-adjustments-and-audit.md`:

- redemption with amount and required reward/note;
- signed manual adjustment with required reason;
- immutable compensating ledger entries for corrections;
- searchable adult audit view for user, job, approval, behaviour, and points actions;
- consistent soft-delete and restore policy for administered entities; and
- tests for negative-balance policy, concurrent redemption, deleted references, and secret redaction.

Exit gate:

- Every balance-changing operation has one ledger entry and one attributable actor.
- Concurrent changes cannot spend the same available points contrary to the selected balance policy.
- Deleted records disappear from normal UI but historical ledger/audit descriptions remain intelligible.
- Audit output never contains PINs or tokens.
- Compose and all tests remain green.

### Phase 7 — Production readiness and home-server delivery

**Goal:** make the piloted product safe to operate, restore, update, and use on target devices.

Deliver from `docs/specs/070-production-readiness.md`:

- UI polish against Kindle Fire Kids tablet, iPhone, and desktop viewports;
- accessibility pass for keyboard, focus, contrast, labels, and touch targets;
- production Compose configuration/override without duplicating the canonical service model;
- Linux `amd64`/`arm64` image decision based on the actual server and a reproducible image build;
- CI/CD deployment with protected secrets, migration gate, health verification, and rollback procedure;
- PostgreSQL backup, restore, retention, and a successfully rehearsed restore;
- log rotation, resource limits, restart policies, and disk-space monitoring guidance;
- dependency/container vulnerability scanning and documented update cadence; and
- operator runbooks in `docs/operations/`.

Exit gate:

- A new server can be deployed from documented steps and reports the same URLs/health state.
- A backup is restored into a fresh database and validated through the UI/API.
- A failed deployment can return to the prior application image without attempting an unsafe database downgrade.
- Required secrets are absent from the repository, image history, and logs.
- The pilot household accepts the key flows on target devices.

## 10. Suggested issue sequence

Create issues just in time, not the whole backlog at once. Within each phase, a useful sequence is:

| Order | Issue shape | Demonstrable result |
| --- | --- | --- |
| 1 | Spec/ADR decision | Product rules and boundaries are accepted |
| 2 | Small walking slice | The thinnest UI → API → PostgreSQL path works |
| 3 | Remaining happy paths | The phase goal is usable |
| 4 | Failure, authorization, and concurrency paths | Unsafe and duplicate operations are prevented |
| 5 | Phase acceptance and docs | Compose demo, automated tests, and operator notes agree |

Keep migrations in the feature issue that requires them. Do not create a generic “all database work” issue or a generic “all UI work” issue.

## 11. Test strategy and phase definition of done

### Test layers

- Domain tests cover invariants and state transitions without a database.
- Application tests cover use-case orchestration and authorization-independent decisions.
- Integration tests use a real PostgreSQL service from the Compose test profile and exercise migrations, constraints, transactions, and HTTP boundaries.
- Web tests cover rendering, validation, authorization-aware navigation, and error states.
- End-to-end smoke tests cover one critical flow per completed phase against the running containers.
- Architecture tests enforce dependency direction and prohibited references where the project split claims a boundary.

Do not replace PostgreSQL integration tests with an EF in-memory provider; its behaviour is not equivalent to PostgreSQL.

### Every phase is done only when

- its accepted spec has no unresolved blocking decisions;
- every acceptance criterion is met;
- changed behaviour has appropriate automated coverage;
- production-target images build without host SDKs;
- `docker compose config --quiet` succeeds;
- the Compose test profile passes;
- all required services become healthy from a clean build;
- a restart preserves expected state;
- the URL banner is correct;
- no secrets or unintended generated artifacts appear in the Git diff; and
- the implementation report lists the exact verification commands and results.

## 12. Product decisions to resolve

Resolve each question in its owning feature spec rather than allowing an implementation agent to guess.

| Decision | Needed by | Recommended MVP default |
| --- | --- | --- |
| Can jobs be assigned to adults, or only children? | Phase 2 | Only children for points-bearing jobs |
| Is an agenda period explicit or inferred from scheduled time? | Phase 2 | Store an explicit period; time remains optional and only orders within the period |
| Can a child have more than one pending submission for a rejected occurrence? | Phase 2/3 | Rejection returns it to a resubmittable state while retaining decision history |
| Are zero-point jobs allowed? Are negative job/behaviour points allowed? | Phase 2 | Allow zero; reject negative awards |
| Can approval override points below zero? | Phase 3 | No; zero or positive only |
| May a balance go negative after redemption or adjustment? | Phase 6 | Redemptions cannot; explicit adult adjustments may, with warning and reason |
| What exactly does monthly recurrence mean for dates absent in shorter months? | Phase 4 | Last valid day of the month; state this visibly |
| How far ahead are recurring occurrences materialized? | Phase 4 | Rolling eight weeks plus on-demand generation |
| How are weekend agenda periods represented? | Phase 4 | Same periods, with independently configurable schedules |
| Can soft-deleted users be restored and can their PIN be reused? | Phase 1/6 | Restore only by an adult; require PIN reset |
| Who performs a new user's first PIN setup? | Phase 1 | Bootstrap adult sets their own; later setup is a one-time handoff initiated from an authenticated adult session |
| Is PIN renewal on activity implemented by token refresh or server-side session records? | Phase 1 | Short-lived JWT plus a revocable server-side session record |
| Which household time zone is canonical? | Phase 0 | Required configuration; local default `Africa/Johannesburg` for development only |
| Which CPU architecture and Linux distribution does the home server use? | Phase 7 | Inspect the target before choosing deployment images |

## 13. Explicitly deferred beyond MVP

- multiple households/tenants;
- public-internet exposure or cloud identity providers;
- native mobile applications and push notifications;
- Kubernetes deployment before the Compose home-server deployment is stable;
- rewards catalog, automated reward purchasing, leaderboards, badges, or casino-style gamification;
- offline-first synchronization;
- microservices, event brokers, or distributed caching; and
- advanced recurrence syntax beyond daily, weekly, and monthly.

## 14. Reference basis

Platform versions should be rechecked when Phase 0 begins. At the time of this plan:

- .NET 10 is an active LTS release: <https://dotnet.microsoft.com/en-us/platform/support/policy>
- PostgreSQL 18 is supported: <https://www.postgresql.org/support/versioning/>
- Node.js 24 is LTS: <https://nodejs.org/en/blog/release/v24.11.0>
- React 19 is stable: <https://react.dev/blog/2024/12/05/react-19>
- Vite's current Node requirements are documented at: <https://vite.dev/guide/>
- Docker Compose health-based dependency ordering is documented at: <https://docs.docker.com/compose/how-tos/startup-order/>
- ASP.NET Core 10 has first-party OpenAPI generation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0>
