# Domain docs

This repository uses a single-context structure with root `CONTEXT.md` and `docs/adr/` directory.

## Layout

- Root `CONTEXT.md` for the project's overall domain model
- `docs/adr/` directory for architectural decision records
- No `CONTEXT-MAP.md` as this is not a monorepo

## Consumer rules

- Read `CONTEXT.md` to understand the core domain concepts
- Browse `docs/adr/` to understand key architectural decisions
- Files in other directories should be referenced by their specific paths

This structure provides a clear, single-source-of-truth approach that works well for most repositories. For larger projects with multiple contexts, consider creating a `CONTEXT-MAP.md`.