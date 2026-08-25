# Family Jobs Board

Family Jobs Board is a family-focused web application for assigning household jobs, approving completed work, and rewarding children with points. It is intended for a single household, runs on the local network, and is designed for children and adults using Kindle Fire tablets, iPhones, or desktop browsers.

## Project status

**Planning and architecture — not yet runnable.**

The repository currently contains the product requirements, domain model, architecture decisions, and a phased [delivery plan](PLAN.md). It does not yet contain a tracked application, Docker Compose stack, API, or UI.

Phase 0 will establish a PostgreSQL-backed walking skeleton with the database, .NET API, and React UI running in separate containers. Do not use ignored build remnants or the old Git stash as an implementation baseline.

## Planned MVP capabilities

- Adult and child profiles protected by role-appropriate PINs.
- Adult administration of users, jobs, and good-behaviour types.
- Daily child agendas with once-off and recurring jobs.
- Child completion submission followed by adult approval or rejection.
- An append-only points history for job awards, good behaviours, redemptions, and adjustments.
- Adult day, week, and month calendar views.
- Audit trails and soft deletion for administered records.
- A warm, low-clutter, touch-friendly interface that remains usable on larger screens.

See [MVP features](docs/mvp-features.md) and the [product brief](docs/product-brief.md) for the detailed product intent.

## Intended technical stack

| Component | Technology |
| --- | --- |
| Web UI | React, TypeScript, and Vite |
| API | ASP.NET Core on .NET |
| Persistence | Entity Framework Core with Npgsql |
| Database | PostgreSQL |
| Local and home-server runtime | Docker Compose |
| API contract | OpenAPI |

Supported versions and dependency-pinning rules are recorded in [PLAN.md](PLAN.md#4-technical-baseline) and should be rechecked when Phase 0 begins.

## Requirements

At the current planning stage:

- Git, to clone and contribute to the repository.
- A Markdown viewer or editor, to review the specifications.

After Phase 0, a clean checkout should require only Docker with the Compose plugin. The .NET and Node.js SDKs will run inside build containers and should not be required on the host.

## Getting started

For a new human checkout, clone the repository and enter it:

```sh
git clone git@github.com:adamavenant/family-jobs-board.git
cd family-jobs-board
```

There are currently no application dependencies to install or services to start. Begin with:

1. [PLAN.md](PLAN.md) for delivery phases, the proposed repository structure, and phase acceptance gates.
2. [CONTEXT.md](CONTEXT.md) for the domain language and rules.
3. [Product brief](docs/product-brief.md) and [MVP features](docs/mvp-features.md) for product behaviour.
4. [Architecture decisions](docs/adr/) for accepted and proposed technical decisions.

### Planned Compose workflow

These commands become available when Phase 0 is delivered; they do not work on the current documentation-only branch.

```sh
# Start the complete stack and print its URLs
docker compose up --build

# Or start in the background, wait for health checks, and print the URLs
./scripts/compose-up.sh

# Run the containerized test suites
./scripts/compose-test.sh

# Stop the stack without deleting PostgreSQL data
./scripts/compose-down.sh
```

The destructive database reset will be explicit and separate from normal shutdown:

```sh
docker compose down --volumes
```

## Planned URLs

The Compose startup banner will print these defaults, with host ports overridable through configuration:

| Resource | URL |
| --- | --- |
| Web application | <http://localhost:3000> |
| API base | <http://localhost:8080/api> |
| OpenAPI document | <http://localhost:8080/openapi/v1.json> |
| API liveness | <http://localhost:8080/health/live> |
| API readiness | <http://localhost:8080/health/ready> |

Browser requests to `/api` will use the web origin and be reverse-proxied to the API container. The direct API port remains available for development and diagnostics.

## Configuration

Phase 0 will add a committed `.env.example` describing every supported setting. Local overrides belong in an untracked `.env` file.

Expected configuration areas include:

- web and API host ports;
- PostgreSQL database name and local-only credentials;
- JWT signing material supplied outside source control; and
- the household time zone.

Never commit real passwords, PINs, tokens, signing keys, or production environment files. Development defaults must not be reused for a home-server deployment.

## Testing and development

The planned test profile uses the real PostgreSQL Compose service rather than an in-memory database substitute. The delivery plan calls for:

- domain and application tests for business rules and use cases;
- API integration tests covering migrations, constraints, transactions, and authorization;
- React component and interaction tests;
- architecture tests for declared dependency boundaries; and
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

Feature-level specifications and operational runbooks will be added under `docs/specs/` and `docs/operations/` as their owning phases begin. The full proposed source and test layout is in [PLAN.md](PLAN.md#5-target-repository-layout).

## Delivery roadmap

Delivery is split into eight independently runnable phases:

0. Containerized PostgreSQL, migration, API, and web walking skeleton.
1. Household bootstrap, sign-in, and user administration.
2. Once-off jobs, daily agenda, and completion submission.
3. Adult approval and the points ledger — the first complete pilot loop.
4. Recurring jobs and the adult calendar.
5. Good behaviours.
6. Redemptions, adjustments, audit, and administration completeness.
7. Production readiness and home-server delivery.

See [PLAN.md](PLAN.md#9-phased-delivery) for deliverables and exit criteria. Work is tracked through [GitHub Issues](https://github.com/adamavenant/family-jobs-board/issues).

## Known limitations and deferred scope

The current repository is not runnable. The MVP also deliberately defers:

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
