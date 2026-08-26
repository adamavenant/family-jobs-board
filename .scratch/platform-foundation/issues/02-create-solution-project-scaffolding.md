# 02 — Create solution/project scaffolding and dependency pinning

**What to build:** The basic project structure with solution files, package management, and dependency pinning.

**Blocked by:** 01 — Accept and document modular-monolith ADR and platform foundation spec

**Status:** ready-for-agent

- [ ] Create `FamilyJobsBoard.sln` 
- [ ] Set up `Directory.Build.props` and `Directory.Packages.props` for dependency pinning
- [ ] Create backend project structure including:
  - `FamilyJobsBoard.Api/`
  - `FamilyJobsBoard.Application/`  
  - `FamilyJobsBoard.Domain/`
  - `FamilyJobsBoard.Infrastructure/`
- [ ] Create web project structure including:
  - `src/web/` with package.json and related files
- [ ] Configure initial packages for .NET, React, PostgreSQL, testing frameworks