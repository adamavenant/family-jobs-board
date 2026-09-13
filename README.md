# Family Jobs Board

Family Jobs Board is a family-focused web application for assigning household jobs, approving completed work, and rewarding children with points. It is intended for a single household, runs on the local network, and is designed for children and adults using Kindle Fire tablets, iPhones, or desktop browsers.

## Project status

**Runnable pilot — core jobs-to-points loop delivered.**

The repository contains a PostgreSQL-backed .NET API and React UI that run as a Docker Compose stack. The current pilot supports household bootstrap and PIN sign-in, family-member administration, dated once-off and daily/weekly/monthly recurring jobs, a navigable daily agenda, child completion submission, adult approval or rejection, and an append-only points history.

The adult calendar and the remaining administration, good-behaviour, redemption, adjustment, audit, and production-readiness work are still outstanding. See the [delivery roadmap](#delivery-roadmap) for the phase-by-phase status.

## MVP capabilities

- Adult and child profiles protected by role-appropriate PINs.
- Adult administration of users, jobs, and good-behaviour types.
- Daily child agendas with once-off and recurring jobs.
- Child completion submission followed by adult approval or rejection.
- An append-only points history for job awards, good behaviours, redemptions, and adjustments.
- Adult day, week, and month calendar views.
- Audit trails and soft deletion for administered records.
- A warm, low-clutter, touch-friendly interface that remains usable on larger screens.

See [MVP features](docs/mvp-features.md) and the [product brief](docs/product-brief.md) for the detailed product intent.

## Technical stack

| Component | Technology |
| --- | --- |
| Web UI | React, TypeScript, and Vite |
| API | ASP.NET Core on .NET |
| Persistence | Entity Framework Core with Npgsql |
| Database | PostgreSQL |
| Local and home-server runtime | Docker Compose |
| API contract | OpenAPI |

Supported versions and dependency-pinning rules are recorded in [PLAN.md](PLAN.md#4-technical-baseline).

## Requirements

A clean checkout requires Git and Docker with the Compose plugin. The .NET and Node.js SDKs run inside build containers and are not required on the host.

## Getting started

For a new human checkout, clone the repository and enter it:

```sh
git clone git@github.com:adamavenant/family-jobs-board.git
cd family-jobs-board
```

Start the complete application from a clean checkout:

```sh
docker compose up --build
```

Then open <http://localhost:3000>. For project context, begin with:

1. [PLAN.md](PLAN.md) for delivery phases, the proposed repository structure, and phase acceptance gates.
2. [CONTEXT.md](CONTEXT.md) for the domain language and rules.
3. [Product brief](docs/product-brief.md) and [MVP features](docs/mvp-features.md) for product behaviour.
4. [Architecture decisions](docs/adr/) for accepted and proposed technical decisions.

### Compose workflow

```sh
# Start the complete stack and print its URLs
docker compose up --build

# Or start in the background and wait for health checks
docker compose up --build --detach --wait

# Validate development and production Compose configuration
./scripts/check-compose-config.sh

# Stop the stack without deleting PostgreSQL data
docker compose down
```

The destructive database reset will be explicit and separate from normal shutdown:

```sh
docker compose down --volumes
```

## URLs

The development Compose override publishes these defaults, with host ports overridable through configuration. The startup banner prints the web and API links.

| Resource | URL |
| --- | --- |
| Web application | <http://localhost:3000> |
| Today API | <http://localhost:8080/api/today> |
| API liveness | <http://localhost:8080/health/live> |
| API readiness | <http://localhost:8080/health/ready> |

OpenAPI is generated with the build and exposed at `/openapi/v1.json` only
when the API runs in the Development environment; the default Compose stack
runs the API in Production mode.

Browser requests to `/api` will use the web origin and be reverse-proxied to the API container. The direct API port remains available for development and diagnostics.

## Configuration

The committed `.env.example` describes the supported settings. Local overrides belong in an untracked `.env` file.

Supported configuration areas include:

- web and API host ports;
- PostgreSQL database name and local-only credentials;
- JWT signing material supplied outside source control; and
- the household time zone.

Never commit real passwords, PINs, tokens, signing keys, or production environment files. Development defaults must not be reused for a home-server deployment.

## Testing and development

API integration tests use disposable real PostgreSQL containers rather than an in-memory database substitute. The test suites include:

- domain and application tests for business rules and use cases;
- API integration tests covering migrations, constraints, transactions, and authorization;
- React component and interaction tests;
- browser-level smoke tests for each completed phase.

Every coding task must be tied to a GitHub issue and implemented as a demonstrable vertical slice. Before changing code, read [AGENTS.md](AGENTS.md), [developer rules](docs/agents/developer-rules.md), [CONTEXT.md](CONTEXT.md), the relevant ADRs, and the assigned issue.

## Documentation map

| Document | Purpose |
| --- | --- |
| [PLAN.md](PLAN.md) | Phased delivery, Compose contract, target structure, test strategy, and open decisions |
| [CONTEXT.md](CONTEXT.md) | Shared domain terminology, relationships, and constraints |
| [Product brief](docs/product-brief.md) | Product purpose, target devices, and experience |
| [MVP features](docs/mvp-features.md) | Detailed MVP roles, models, and workflows |
| [Non-functional requirements](docs/mvp-nonfunctional-reqs-and-architecture.md) | Application flow, security posture, architecture, and UI direction |
| [Architecture decisions](docs/adr/) | Accepted, proposed, and future architectural decisions |
| [Agent guidance](docs/agents/) | Issue workflow, domain-document conventions, triage labels, and developer rules |

Feature-level specifications and operational runbooks live under `docs/specs/` and `docs/operations/`. The full source and test layout is described in [PLAN.md](PLAN.md#5-target-repository-layout).

## Delivery roadmap

Delivery is split into eight independently runnable phases:

| Phase | Status | Delivered and outstanding work |
| --- | --- | --- |
| 0 — Platform foundation | Delivered | PostgreSQL, migrations, API, web UI, Compose health checks, real-PostgreSQL integration tests, and CI are operational. |
| 1 — Identity and users | Delivered | Household bootstrap, PIN authentication and sessions, member onboarding/lifecycle, and secure PIN reset are implemented. |
| 2 — Jobs and daily agenda | In progress | Dated once-off job creation, daily navigation, multi-child assignment, and completion submission are delivered. Job edit/cancel/delete and the adult child filter remain outstanding. |
| 3 — Approvals and points | Core loop delivered | Adults can approve or reject submitted work and approved jobs create append-only ledger entries shown in the child's balance/history. Approval-time point override remains outstanding. |
| 4 — Recurring jobs and calendar | In progress | Daily, weekly, and monthly creation with duplicate-safe occurrence materialization is delivered. Series pause/end/edit and adult day/week/month calendar views remain outstanding. |
| 5 — Good behaviours | Planned | Not implemented. |
| 6 — Redemptions, adjustments, audit, and administration completeness | Planned | Not implemented. |
| 7 — Production readiness and home-server delivery | In progress | CI publishes deployable images and the home-server deployment path exists; the broader readiness, backup/restore, accessibility, and operations exit gate remains outstanding. |

See [PLAN.md](PLAN.md#9-phased-delivery) for deliverables and exit criteria. Work is tracked through [GitHub Issues](https://github.com/adamavenant/family-jobs-board/issues).

## Known limitations and deferred scope

The runnable pilot still deliberately defers:

- multiple households or tenants;
- public-internet hosting and external identity providers;
- native mobile applications and push notifications;
- Kubernetes until the Compose deployment is stable;
- offline synchronization;
- microservices and event brokers; and
- leaderboards, badges, and casino-style gamification.

## Contributing and support

Use [GitHub Issues](https://github.com/adamavenant/family-jobs-board/issues) to propose work or report a problem. Coding work should:

1. start from an issue with explicit acceptance criteria;
2. use an issue branch created from `main`;
3. preserve unrelated worktree changes;
4. include the relevant tests, configuration, and documentation; and
5. leave the complete Compose stack healthy.

The authoritative workflow is in [docs/agents/developer-rules.md](docs/agents/developer-rules.md).

## Licence

No licence file has been added yet. A licence must be selected before describing the repository as open source or redistributing it.
