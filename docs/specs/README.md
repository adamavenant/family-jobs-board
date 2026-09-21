# Feature specifications

Feature specs normally precede implementation issues as required by
`PLAN.md`. The first pilot slices were delivered before that rule was applied
consistently, so issue #64 backfilled retrospective specs without pretending
they were written in advance.

| Phase | Specification status |
| --- | --- |
| 0 — Platform foundation | Retrospective spec: `000-platform-foundation.md` |
| 1 — Identity and users | Accepted evolving spec: `010-identity-and-users.md` |
| 2 — Jobs and agenda | Retrospective spec: `020-jobs-and-agenda.md` |
| 3 — Completions, approvals, and points | Retrospective spec: `030-completions-approvals-and-points.md` |
| 4 — Recurring jobs and calendar | Retrospective spec: `040-recurring-jobs-and-calendar.md` |
| 5 — Good behaviours | Implemented with issue #81: `050-good-behaviours.md` |
| 6 — Redemptions, adjustments, and audit | No implementation has merged; write `060-redemptions-adjustments-and-audit.md` before issues are cut. |
| 7 — Production readiness | Historical exception below; write `070-production-readiness.md` before further Phase 7 feature issues are cut. |

## Phase 7 historical exception

The GHCR image-publication and LAN deployment automation slices merged before
a Phase 7 feature spec existed. This is recorded as a historical process
exception, not approval of the remaining Phase 7 behavior. The existing
operator contract is documented in `docs/operations/lan-deployment.md`; backup
and restore, accessibility, monitoring, security review, and the remaining
production-readiness work still require `070-production-readiness.md` before
implementation issues are created.
