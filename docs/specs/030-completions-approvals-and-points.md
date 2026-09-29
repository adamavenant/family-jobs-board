# Completions, approvals, and points

## Status

Retrospective specification of the delivered core Phase 3 loop. Reviewer
attribution and approval-time point override remain planned.

## Outcome and user value

Adults can complete and approve work on a child's behalf in one action, or
approve or reject child-submitted work. Approval awards the job's configured
points exactly once; children see a running balance and job-based earning
history. Rejection returns the job for another attempt and may include feedback.

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Complete work | No | Own open jobs (submits for approval) | Any open child job (approves and awards) |
| Approve or reject pending work | No | No | Yes |
| Read points balance/history | No | Own | Any child, via the points ledger view (issue #77) |

## Scope

Delivered scope includes child and adult-on-behalf completion submission,
pending state, adult controls on the daily board,
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

An open job may be submitted by its assigned child or completed by an adult
acting on the child's behalf. Child submission uses the `Open` to
`PendingApproval` transition. Adult completion atomically performs completion
and approval, records both UTC instants and an approved review decision, and
creates the job's single points-ledger award. The adult does not need to perform
a second approval action. The adult request includes the points value shown in
its confirmation; if the job's points changed meanwhile, the server rejects
the stale confirmation without changing the job or ledger.

Both adult award paths require the points value shown to the adult. Approval
of child-submitted work applies the same stale-confirmation guard as adult
completion, so an intervening edit cannot silently increase the award.

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

- `POST /api/jobs/{id}/approve` requires `expectedPoints` and returns the
  approved job and updated balance when it still matches.
- `POST /api/jobs/{id}/reject` accepts an optional reason and returns the open
  job with its latest rejection.
- `GET /api/today` exposes pending count to adults and the balance to the
  authenticated child. Earning history moved to `GET /api/points-ledger` with
  issue #77; see the points ledger view in
  `060-redemptions-adjustments-and-audit.md`.

Missing jobs and jobs whose assigned child is unavailable return `404` across
complete, approve, reject, edit, and cancel. Invalid, concurrently changed, or
already-decided transitions and duplicate awards return `409`; an overlong
rejection reason or missing adult completion confirmation returns validation
errors.

## UI states

Adults see pending jobs and approve/reject controls in the daily agenda.
Completing on a child's behalf first shows an inline confirmation naming the
child and points award; confirming still completes, approves, and awards in one
server operation, while keeping the job open makes no request. Initial focus
lands on the safe Keep open action, and the prompt closes if revalidation
changes the job's status or points.
Rejection feedback remains in the form if submission fails. Children see
pending, rejected-for-retry, and approved states, plus their balance, which
opens their newest-first points ledger. Loading, empty, submitting, success,
and error states are covered.

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
- Given an adult completes an open job on behalf of its child, then completion,
  approval, the review decision, and one ledger award commit atomically.
- Given the job's points change after the adult opens confirmation, then the
  stale completion or approval is rejected and no decision or award is written.
- Given two approval attempts race, then at most one award exists for the job.
- Given a pending job is rejected, then it becomes open, no points are awarded,
  and optional feedback is visible to the child.
- Given a child views the board after restart, then balance equals the sum of
  persisted awards and the earning history names each source job.

## Automated tests

Domain tests cover approve/reject state guards and decision validation.
Application and integration tests cover atomic awards, duplicate prevention,
rejection/retry, authorization, balance, and history. Component and Playwright
tests cover the child-to-adult approval loop, adult-on-behalf completion, and
rejection errors.

## Compose demonstration

Start <http://localhost:3000>, submit an assigned job as its child, sign in as
an adult to approve or reject it, then return as the child to inspect state,
balance, and history. Restart Compose to demonstrate persistence.

## Unresolved decisions

Before Phase 3 is complete, an owning issue must define reviewer attribution,
approval-time point override rules, and whether a separate cross-date approval
queue is required. These are planned behavior, not properties of the current
schema.
