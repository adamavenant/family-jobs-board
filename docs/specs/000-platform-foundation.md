# Platform foundation

## Status

Retrospective specification of the delivered Phase 0 foundation.

## Outcome and user value

A clean checkout builds and runs a PostgreSQL-backed API and React application
through one Docker Compose contract. The same topology supports local
development, automated checks, and the LAN deployment.

## Actors and authorization

The liveness and readiness endpoints are anonymous. Product data endpoints use
the authentication and role policies specified in
`010-identity-and-users.md`. Operators control configuration, migrations, and
deployment outside the product UI.

## Scope

Delivered scope includes the .NET modular monolith, React SPA, PostgreSQL,
forward-only EF Core migrations, OpenAPI generation, same-origin `/api`
proxying, health checks, Compose validation, real-PostgreSQL integration tests, CI, and
production image publication. Backup/restore rehearsal, monitoring, and the
complete production-readiness gate remain Phase 7 work.

## Domain rules and failure cases

Phase 0 introduces no household business rules. PostgreSQL is the production
and integration-test store. The API never creates its schema at startup; the
`migrate` service must finish successfully first. Readiness fails while the
database cannot be queried, while liveness reports only process health.

## Data and migration

Checked-in EF Core migrations are forward-only and applied by the dedicated
`migrate` image. The named `postgres-data` volume survives normal Compose
shutdown. Database reset requires the explicit destructive `down --volumes`
operation.

## HTTP contract

- `GET /health/live` reports process liveness.
- `GET /health/ready` verifies database readiness.
- `/openapi/v1.json` publishes the generated API contract when the API runs in
  the Development environment; production mode does not expose it.
- Browser `/api` requests are proxied by the web container to the private API.

Failures use HTTP status codes and Problem Details where an API operation has a
structured application error.

## UI states

The web image serves the client-rendered application, a loading route, and
route-level error states. Product-specific empty, populated, validation, and
narrow-screen states belong to their feature specs.

## Audit and security

Containers run without embedded production credentials. Production secrets
are supplied outside Git. The database and API stay private in the production
Compose overlay, with only the web entry point published to the LAN.

## Observability and health

Compose orders database health, migration completion, API readiness, and web
health. API logs support diagnosis. CI validates
backend tests, web checks, and Compose configuration before main-branch images
are published.

## Acceptance scenarios

- Given a clean checkout with Docker, when `docker compose up --build` runs,
  then migration, API, and web reach their declared healthy states.
- Given an existing database volume, when the stack restarts, then schema and
  application data remain present.
- Given PostgreSQL is unavailable, when readiness is queried, then readiness
  fails without changing schema or data.

## Automated tests

Backend tests cover application/domain behavior and API integration against
disposable PostgreSQL containers. Web checks run formatting, linting, strict type checking, component
tests, and a production build. Playwright covers critical phone/tablet journeys,
and Compose configuration is validated in CI.

## Compose demonstration

Run `docker compose up --build`, open <http://localhost:3000>, and query
<http://localhost:8080/health/live>,
<http://localhost:8080/health/ready>. Run the API in Development mode to inspect
`/openapi/v1.json`. Run
`./scripts/check-compose-config.sh` to validate development and production
Compose configuration.

## Unresolved decisions

No Phase 0 decision remains open. Backup/restore, monitoring, and remaining
operator controls are owned by Phase 7 and must be resolved before that phase's
exit gate is declared complete.
