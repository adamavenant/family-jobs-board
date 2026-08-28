# Developer Rules

## Before work

- Coding requires a GitHub issue. Before edits, state issue and acceptance criteria; none: stop.
- Preserve worktree. Fetch origin; fast-forward `main`; branch `issue-<number>-<kebab-case-title>` from it. Never commit to `main`.
- Read `CONTEXT.md`, issue, relevant ADRs.
- Assigned issue blocked: stop; ask user for next steps.
- Limit changes to acceptance criteria plus required tests, docs, config. Ask before dependencies, restructuring, architectural changes.

## During work

- As each acceptance criterion passes, check it off on the GitHub issue.

## Safety

- Verify sandbox before project commands.
- No host tools or machine-wide changes without permission.
- Never expose or commit credentials, secrets, local environment files.
- Edit `README.md` only when explicitly asked.

## Done

- Acceptance criteria met; changed behaviour tested.
- All relevant build, test, lint, type-check commands pass.
- Images build; required Compose services healthy.
- Diff contains intended changes only.
- Push issue branch; open PR when review-ready.
- Report verification commands and results.

## PR review

- Read every conversation comment, review summary, inline comment. Inaccessible: notify user; do not claim review complete.
- Valid and safe: implement, verify, push. Otherwise reply with rationale.

## Back-end

- Baseline: .NET 10 LTS, C# 14, Minimal APIs, EF Core 10/Npgsql. Follow pinned versions; no unrelated upgrades.
- Feature-first slices. `Domain`: invariants; `Application`: use cases/ports; `Infrastructure`: adapters; `Api`: HTTP/composition. Domain/Application never reference ASP.NET Core or EF Core.
- Business rules in Domain/Application, not endpoints or persistence mappings. Nullable references enabled.
- Records for DTOs/value objects; classes for entities/aggregates. Async I/O takes `CancellationToken`; no `.Result`, `.Wait()`, sync wrappers.
- Prefer `TypedResults` plus declared `Results<T...>`. RFC Problem Details for errors; never success with `null`.
- PostgreSQL from first increment. Checked-in forward-only EF migrations; no `EnsureCreated`, startup schema mutation, or EF InMemory substitutes.
- Persist instants in UTC; calculate schedules in configured household time zone.
- Structured, correlated logs; no secrets/PINs.
- Domain tests for invariants; application tests for orchestration; integration tests exercise HTTP, migrations, constraints, transactions against real PostgreSQL.

## Front-end

- React 19, strict TypeScript, Vite; no Create React App. Follow lock file; no unrelated upgrades/floating versions.
- Client-rendered SPA over existing .NET API. Next.js, server React, React Server Components, new server runtime require an ADR.
- React Router Data Mode for routes/loading/actions/errors/code splitting. Local state first; URL for shareable state; Context only when shared; global state only with proven need.
- No API fetch in `useEffect`. Use route loaders/actions or query layer; server-state library only for proven caching/sync needs.
- API calls outside presentation components; use generated contract types.
- Semantic HTML/native controls. Keyboard, visible focus, labels, no hover-only actions. Verify required tablet/phone widths.
- Vitest + Testing Library for behaviour; Playwright for critical journeys. Before completion: format, lint, type-check, test, production build.

## API contract

- OpenAPI is published API/UI contract. Explicit feature-owned DTOs; no EF/domain types over HTTP. Contracts assembly only for proven .NET consumers/module boundaries.
- Generate OpenAPI 3.1 at build; expose same document and interactive docs in development only.
- Document operation, parameters, body, success/error responses, auth.
- Generate TypeScript types/client; never duplicate, hand-edit, or weaken with `any`/unchecked casts.
- Wire dates/timestamps are ISO 8601 strings; parse/format at UI boundary.
- Commit deterministic generated output. Regenerate on contract changes; CI fails on drift.
