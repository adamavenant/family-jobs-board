# 03 — Set up PostgreSQL, migration, API, web, and app-links Compose services

**What to build:** The complete Docker Compose setup with all required services.

**Blocked by:** 02 — Create solution/project scaffolding and dependency pinning

**Status:** ready-for-agent

- [ ] Create `compose.yaml` with:
  - `db` service using PostgreSQL 18
  - `migrate` service that runs EF Core migrations
  - `api` service for the ASP.NET Core API 
  - `web` service for the React SPA
  - `app-links` service that prints useful host URLs
- [ ] Configure required service dependencies and health checks
- [ ] Set up Docker Compose environment variables for ports and connection strings