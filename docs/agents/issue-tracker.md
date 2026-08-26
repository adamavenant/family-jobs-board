# Issue tracker

This repository uses GitHub Issues for tracking work. See `docs/agents/issue-tracker.md` for more information.

## Configuration

- Tracker: GitHub
- CLI tool: `gh`
- PRs as a request surface: off

GitHub Issues is the authoritative ticket store for this repository. When a
workflow publishes tickets, do not fall back to local files if GitHub is
unavailable.

## Publishing preflight

Before creating or modifying issues, verify all of the following from the
repository root:

1. `command -v gh` succeeds.
2. `gh auth status --hostname github.com` reports an active authenticated account.
3. `gh repo view --json nameWithOwner` resolves to `adamavenant/family-jobs-board`.

If any check fails, stop and tell the user which prerequisite is missing. Do
not create tickets under `.scratch` or use another tracker as a fallback.

## Workflow

1. Run the publishing preflight.
2. Create new issues using `gh issue create`, in dependency order. Pass existing
   blocking issue numbers with `--blocked-by` so GitHub records native issue
   dependencies.
3. Track issues via the GitHub web interface.
4. Use GitHub's labeling and project board features for organization.
