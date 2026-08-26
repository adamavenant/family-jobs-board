# 01 — Accept and document modular-monolith ADR and platform foundation spec

**What to build:** The accepted modular-monolith ADR and platform foundation spec that defines the architecture and technical baseline for the application.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] Accept `docs/adr/0002-modular-monolith.md` 
- [ ] Create `docs/specs/000-platform-foundation.md` with:
  - Platform baseline including .NET 10, PostgreSQL 18, React 19
  - Technical requirements and dependency pinning
  - Repository layout as defined in PLAN.md
  - Docker Compose contract defining services and health checks