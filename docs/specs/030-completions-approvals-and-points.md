# Completions, approvals, and points

## Status

Retrospective specification of the delivered core Phase 3 loop. Reviewer
attribution and approval-time point override remain planned.

## Outcome and user value

Adults can approve or reject child-submitted work. Approval awards the job's
configured points exactly once; children see a running balance and job-based
earning history. Rejection returns the job for another attempt and may include
feedback.

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Submit owned work | No | Yes | No |
| Approve or reject pending work | No | No | Yes |
| Read points balance/history | No | Own | Not exposed as an adult points view |

## Scope

Delivered scope includes pending state, adult controls on the daily board,
approve/reject decisions, optional rejection feedback, one job-sourced ledger
award per approval, balance calculation, and child earning history. A separate
queue, reviewer identity, approval reason, point override, redemptions,
and adjustments are not implemented. Good-behaviour awards were added to the
same ledger in Phase 5; see `050-good-behaviours.md`.

## Domain rules and state

Only `PendingApproval` jobs can be approved or rejected. Approval moves the job
to `Approved`, records a UTC instant, persists an approved decision, and adds a
positive ledger award equal to the job points in one transaction. A unique
database constraint on job ID prevents duplicate awards.

Rejection records a decision with optional trimmed feedback of at most 500
characters, returns the job to `Open`, and clears its completion instant. It
does not award points. Previous decisions remain historical records, while the
latest rejection is displayed for the next attempt.

## Data and migration

`job_review_decisions` stores job, outcome, optional reason, and UTC decision
time. `points_ledger_entries` stores child, source job, non-negative amount,
and UTC award time, with one entry per job. Current balance is calculated as
the sum of entries rather than stored on the child. The delivered schema does
not record the reviewing adult.

## HTTP contract

- `POST /api/jobs/{id}/approve` returns the approved job and updated balance.
- `POST /api/jobs/{id}/reject` accepts an optional reason and returns the open
  job with its latest rejection.
- `GET /api/today` exposes pending count to adults and balance/earning history
  to the authenticated child.

Missing jobs return `404`; invalid or already-decided transitions and duplicate
awards return `409`; an overlong rejection reason returns validation errors.

## UI states

Adults see pending jobs and approve/reject controls in the daily agenda.
Rejection feedback remains in the form if submission fails. Children see
pending, rejected-for-retry, and approved states, plus balance and newest-first
earnings. Loading, empty, submitting, success, and error states are covered.

## Audit and security

Adult role authorization is enforced by the API. Decisions and awards are
immutable history, but reviewer attribution and the broader audit event model
are not yet implemented and must not be inferred from the authenticated UI.

## Observability and health

No new health endpoint is required. Transaction/constraint failures surface as
stable conflict responses, and the standard readiness check covers PostgreSQL.

## Acceptance scenarios

- Given a pending job, when an adult approves it, then the job, decision, and
  one ledger award commit together and the child's balance increases once.
- Given two approval attempts race, then at most one award exists for the job.
- Given a pending job is rejected, then it becomes open, no points are awarded,
  and optional feedback is visible to the child.
- Given a child views the board after restart, then balance equals the sum of
  persisted awards and the earning history names each source job.

## Automated tests

Domain tests cover approve/reject state guards and decision validation.
Application and integration tests cover atomic awards, duplicate prevention,
rejection/retry, authorization, balance, and history. Component and Playwright
tests cover the child-to-adult approval loop and rejection errors.

## Compose demonstration

Start <http://localhost:3000>, submit an assigned job as its child, sign in as
an adult to approve or reject it, then return as the child to inspect state,
balance, and history. Restart Compose to demonstrate persistence.

## Unresolved decisions

Before Phase 3 is complete, an owning issue must define reviewer attribution,
approval-time point override rules, and whether a separate cross-date approval
queue is required. These are planned behavior, not properties of the current
schema.
