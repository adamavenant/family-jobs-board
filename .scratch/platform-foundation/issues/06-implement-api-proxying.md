# 06 — Implement same-origin /api proxying from web to API container

**What to build:** A reverse proxy setup that allows same-origin API calls from the web container.

**Blocked by:** 03 — Set up PostgreSQL, migration, API, web, and app-links Compose services

**Status:** ready-for-agent

- [ ] Configure web container to proxy `/api` requests to API container
- [ ] Implement middleware or configuration for same-origin request handling
- [ ] Verify API calls work through the proxy in development environment