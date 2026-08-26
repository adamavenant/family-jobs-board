# 12 — Implement CI that builds images, runs tests, starts Compose, checks health, and tears it down

**What to build:** Continuous Integration pipeline that validates the entire stack.

**Blocked by:** 09 — Setup unit, integration, and web smoke test containers under test profile

**Status:** ready-for-agent

- [ ] Create CI configuration (GitHub Actions workflow)
- [ ] Configure jobs to build Docker images
- [ ] Set up test execution with proper Compose environment
- [ ] Add health checking for services 
- [ ] Implement cleanup procedure that preserves logs on failure
- [ ] Verify CI pipeline runs successfully end-to-end