# 0002 - Adopt a Modular Monolith

## Status

Accepted

## Context

Family Jobs Board needs clear boundaries as jobs, approvals, points, rewards, identity, and audit behaviour grow. It also needs simple local deployment: one household, one API, one PostgreSQL database, and no distributed-system infrastructure.

An unstructured monolith risks coupling every feature to every other feature. Microservices would add network calls, independent deployments, distributed data, and operational overhead before the domain boundaries are stable.

## Decision

Build the back end as a modular monolith: one deployable API and one database, divided internally into cohesive business modules with explicit boundaries.

### Mental model

- **Monolith** describes deployment: modules run in one process and ship together.
- **Module** describes business ownership: one capability, its language, rules, use cases, and data. It approximates a DDD bounded context.
- **Layer/project** describes technical responsibility: Domain, Application, Infrastructure, or API.
- **Vertical slice** describes delivery: one user capability implemented through all required layers.

Modules and layers are different axes. A Jobs module may have domain types in `Domain`, use cases in `Application`, persistence in `Infrastructure`, and endpoints in `Api`. Avoid grouping unrelated business capabilities merely because they use the same technology.

```text
React SPA
    |
HTTP API and composition root
    |
+---+------------------------------+
| business modules, one process    |
| Identity | Jobs | Points | ...   |
+---+------------------------------+
    |
one PostgreSQL database
each table has one module owner
```

Initial module boundaries are candidates, refined through feature specifications and domain learning. Likely capabilities include Identity and Users, Jobs and Agenda, Recognition and Points, and Rewards and Administration. Record material boundary changes by updating this ADR or adding another.

### Boundary rules

- Organize code by business capability, then by feature/use case.
- Each module owns its domain model, behaviour, application use cases, and persisted data.
- A module never reads or writes another module's tables or exposes its domain/EF entities.
- Cross-module interaction uses a small explicit contract: an application interface, command/query, or event. Calls remain in-process; no internal HTTP or message broker by default.
- Cross-module reads use public queries or an explicitly owned read model. Cross-module workflows are orchestrated explicitly.
- Shared code stays minimal and domain-neutral. No miscellaneous `Shared`, `Common`, or `Helpers` dumping ground.
- Keep API DTOs with their feature. Add a `*.Contracts` assembly only for a proven .NET consumer or boundary that benefits from compile-time enforcement.

### Physical structure and enforcement

Start with the solution boundaries in `PLAN.md`:

- `Domain`: aggregates, value objects, invariants, domain events; no ASP.NET Core or EF Core references.
- `Application`: use cases and ports; no infrastructure implementation.
- `Infrastructure`: persistence and external adapters.
- `Api`: HTTP adapter and composition root.

Do not create one assembly per module automatically. Use feature folders, `internal` visibility, dependency direction, and architecture tests first. Split an assembly only when it makes a boundary clearer or enforceable.

Architecture tests guard references and visibility; integration tests cover public contracts and data boundaries. Code review still matters: a modular monolith depends on discipline as well as folders.

Modules are not independently deployed or scaled. A future extraction to a service is possible if evidence justifies it, but is not a goal or promise of this decision.

## Alternatives considered

### Unstructured or layered monolith only

Simpler initially, but technical layers alone do not protect business boundaries. Features can become coupled through shared services, entities, and database access.

### Microservices

Allow independent deployment and scaling, but add network failure, service discovery, distributed observability, data consistency, and operational cost. Unnecessary for one household and premature while boundaries are still being learned.

### Assembly per module from day one

Provides stronger compile-time isolation, but creates structural overhead before the useful boundaries are known. Assemblies may be introduced incrementally.

## Consequences

### Positive

- One deployment and database keep development and operations simple.
- Business capability ownership improves cohesion and discoverability.
- Explicit contracts limit accidental coupling and make changes easier to reason about.
- In-process calls and local transactions avoid distributed-system cost.
- Boundaries can be strengthened or extracted later when evidence supports it.

### Negative

- Boundaries require sustained discipline, reviews, and architecture tests.
- The whole API is deployed and scaled together; one module can affect the process.
- Some workflows and reports cross module boundaries and need explicit orchestration/read models.
- More structure and mapping than an unstructured monolith.

### Neutral

- Modules share one physical PostgreSQL database, but not table ownership.
- This decision does not require CQRS, event sourcing, a mediator, separate database schemas, or asynchronous messaging.
- Module boundaries and assembly layout may evolve through later ADRs without changing the single-deployment decision.

## References

- [Microsoft: Common web application architectures](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Martin Fowler: Monolith First](https://martinfowler.com/bliki/MonolithFirst.html)
- [Kamil Grzybek: Modular Monolith with DDD reference implementation](https://github.com/kgrzybek/modular-monolith-with-ddd)
