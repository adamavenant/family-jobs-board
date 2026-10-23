# Completions, approvals, and points

## Status

Retrospective specification of the delivered core Phase 3 loop. Reversible
approvals were added with issue #138; see "Reversing an approval" below. Reviewer
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
pending state, adult controls on the daily board, a household review queue
across all dates (issue #137), approve/reject decisions, optional rejection
feedback, one job-sourced ledger award per approval, balance calculation, and
child earning history. Reviewer identity, approval reason, and point override
are not implemented. Good-behaviour awards were added to the
same ledger in Phase 5; see `050-good-behaviours.md`.

## Domain rules and state

Only `PendingApproval` jobs can be approved or rejected. Approval moves the job
to `Approved`, records a UTC instant, persists an approved decision, and adds a
positive ledger award equal to the job points in one transaction. Each approval
is numbered, and a unique database constraint on the job and approval number
prevents duplicate awards. Approval is no longer final; see "Reversing an
approval".

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

`job_review_decisions` stores job, outcome (`Approved`, `Rejected`, or
`ApprovalReversed`), optional reason, and UTC decision time.
`points_ledger_entries` stores child, source job, non-negative amount, and UTC
award time, with one award per approval of the job; an approval reversal adds a
second, negative entry for the job (see below). Current balance is calculated as
the sum of entries rather than stored on the child. The delivered schema does
not record the reviewing adult.

## HTTP contract

- `POST /api/jobs/{id}/approve` requires `expectedPoints` and returns the
  approved job and updated balance when it still matches.
- `POST /api/jobs/{id}/reject` accepts an optional reason and returns the open
  job with its latest rejection.
- `GET /api/today` exposes the pending count to adults and the balance to the
  authenticated child. The pending count is household-wide: it counts every
  `PendingApproval` job for active children whatever its scheduled date, and
  ignores the viewed date and child filter, so work submitted on an earlier
  day stays counted until it is decided. `GET /api/calendar` returns the same
  count.
- `GET /api/approvals/pending` is adult-only and lists every pending job for
  active children across all dates, oldest scheduled date first and in agenda
  order within a date, using the shared `JobResponse` shape. Children get
  `403`. Earning history moved to `GET /api/points-ledger` with
  issue #77; see the points ledger view in
  `060-redemptions-adjustments-and-audit.md`.

Missing jobs and jobs whose assigned child is unavailable return `404` across
complete, approve, reject, edit, and cancel. Invalid, concurrently changed, or
already-decided transitions and duplicate awards return `409`; an overlong
rejection reason or missing adult points confirmation returns validation errors.

## UI states

Adults see pending jobs and approve/reject controls in the daily agenda. The
"awaiting review" badge on the daily board and the calendar is a button that
opens a non-modal side panel (full width on phones) listing every pending job,
grouped by scheduled date, with Approve and Reject (optional reason) for each.
Decisions reuse the approve/reject operations and refresh the page in place, so
grown-ups never navigate away to review. Focus moves to the panel heading when
it opens and stays in the panel as decided jobs leave the list; Escape or the
close button returns focus to the badge.
Completing on a child's behalf first shows an inline confirmation naming the
child and points award; confirming still completes, approves, and awards in one
server operation, while keeping the job open makes no request. Initial focus
lands on the safe Keep open action, and the prompt closes if revalidation
changes the job's status or points.
Completing, approving, and rejecting wait behind the undo toast described in
`100-undo-and-household-settings.md`: the job shows its new state at once and
is saved when the toast expires. A refusal after the undo time is reported in
an error toast and the job shows the server's state. Children see
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
- Given two approval attempts race, then at most one award exists for that
  approval of the job.
- Given a pending job is rejected, then it becomes open, no points are awarded,
  and optional feedback is visible to the child.
- Given a child views the board after restart, then balance equals the sum of
  persisted awards and the earning history names each source job.

## Automated tests

Domain tests cover approve/reject state guards and decision validation.
Application and integration tests cover atomic awards, duplicate prevention,
rejection/retry, authorization, balance, and history. Application and
PostgreSQL integration tests also cover the review queue's cross-date
ordering, exclusion of cancelled jobs and inactive children, and the
household-wide pending count on the daily board and calendar. Component and
Playwright tests cover the child-to-adult approval loop, adult-on-behalf
completion, rejection errors, and reviewing from the side panel at phone,
tablet, and desktop widths.

## Reversing an approval

Issue #138. Approving a job by mistake, such as approving a job the adult meant
to cancel, can be undone at any time. The undo is not limited to a grace period.

### Rules

- Only an adult can reverse, and only an `Approved` job. Children get `403`.
- The job returns to how it was before it was approved:
  - a job the child submitted returns to `PendingApproval` and keeps its
    completion time;
  - a job an adult marked done for the child returns to `Open`, with its
    completion and approval times cleared.

  `jobs.completed_by_adult` records which case applies. Adult completion sets
  it as part of the same transition that approves the job.
- The reversal is recorded as an `ApprovalReversed` review decision. It doesn't
  record the deciding adult yet (#122).
- A new negative ledger entry for the job takes back the award; the award is
  never edited or deleted. The entry's `reversed_entry_id` names the award it
  takes back and its amount is the award's, negated. A zero-point award is
  taken back with a zero entry.
- The job can then be approved again, rejected, edited, or cancelled. An open
  job can be completed again. Each approval is numbered by
  `jobs.approval_count`, and its award carries that number in
  `points_ledger_entries.job_approval_number`, so approving again awards the
  points once more, exactly once.
- **Never negative.** A reversal is a points removal. It takes the child's points
  lock (see `060-redemptions-adjustments-and-audit.md`) before reading the job,
  so it judges the job as it is now. Under the lock it refuses with `409` if the
  child's balance is below the award, for example because the points were
  redeemed, and nothing changes. A zero-point award takes nothing, so it can
  always be reversed. The response re-reads the balance after saving, since
  awards don't take the lock.
- **History keeps its names.** A reversed job can be edited again, so every job
  ledger entry records the job's name when it is written (`job_name`). An
  award and its reversal keep the name the job had then, even if the job is
  renamed before it is approved again.
- **Stale confirmation.** The adult confirms the points shown to them. If they
  no longer match the award, the reversal is refused like a stale approval.

### Data and migration

`MakeJobApprovalsReversible`:

- adds `jobs.approval_count` (integer, default 0) and `jobs.completed_by_adult`
  (boolean, default false), with `ck_jobs_approval_tracking`. That check
  requires a non-negative count, at least one approval for an `Approved` job,
  and `completed_by_adult` only on an `Approved` job;
- adds `points_ledger_entries.job_approval_number`, `job_name`, and
  `reversed_entry_id` (a restricting self-reference).
  `ck_points_ledger_entries_job_entry` makes a job entry either an award with an
  approval number of at least 1, or a reversal with a reversed entry and no
  number. `ck_points_ledger_entries_job_name` requires a name on exactly the
  job entries. The amount-sign check allows `<= 0` for reversals;
- replaces `ux_points_ledger_entries_job_id` with
  `ux_points_ledger_entries_job_approval` on `(job_id, job_approval_number)` and
  `ux_points_ledger_entries_reversed_entry_id`. Two racing approvals or two
  racing reversals of the same award can't both commit, and job-row optimistic
  concurrency serializes every job transition;
- backfills each existing award as approval 1 with its job's name (unchanged
  since approval, because approved jobs couldn't be edited), every approved job
  (or job with an award) with one approval, and `completed_by_adult` where
  completion and
  approval share an instant. That is always true for adult completion, which
  approves at the instant it completes, and never true for a separately
  approved submission;
- refuses to downgrade while any reversal exists, since one award per job could
  no longer hold.

"Reset jobs and points" deletes reversals with the other ledger entries.

### HTTP contract

`POST /api/jobs/{id}/reverse-approval?expectedPoints={n}`, adult-only:

- `200` with `{ job, pointsBalance }`, the restored job and the child's new
  balance;
- `400` when `expectedPoints` is missing;
- `403` for children;
- `404` when the job or its active child is unavailable;
- `409` when the job isn't approved, was already reversed, or changed
  concurrently, or when `expectedPoints` no longer matches the award;
- `409` with `code: "insufficientPoints"` and `currentBalance` when the child
  doesn't have the points. The detail names the child, their balance, and the
  points.

### UI

Approved job cards give adults a **Reverse approval** action. It opens an inline
confirmation naming the child and the points to take back, with initial focus
on **Keep approved**. Keeping it, or pressing Escape, makes no request and
returns focus to the action. Confirming submits the `reverseApproval` intent on
the board's route action through the job card's fetcher, as approve and reject
do. When it succeeds, focus moves to the job's heading, because the card's
controls change. The prompt closes if revalidation changes the job's status or
points. Unlike other refusals, a refused reversal revalidates the board, since
there is no typed input to keep and the job has often changed under a stale
card. The server's message stays on the card. The points ledger marks the reversal entry
**Approval reversed** next to the job's name, and the award stays listed.

### Tests

- Domain: both restorations, follow-on approve, reject, cancel, edit, and
  re-completion, guards, and the reversal ledger entry.
- Application: reading the job under the lock, re-approval numbering, the
  insufficient-points refusal, zero-point awards, the latest rejection on a
  reopened job, the balance after saving, stale and missing confirmations, and
  children.
- PostgreSQL integration: the endpoint for both restorations, re-approval,
  names kept after a rename, reject and cancel after reversal, redeemed points, racing reversals, a
  reversal racing a redemption, `403` and `404`, constraints, reset, and the
  migration backfill and downgrade guard.
- Vitest: the confirmation and its focus, Escape, submission and focus after it,
  refusal with revalidation, a job already reversed elsewhere, prompt closing
  on revalidation, children, and the ledger label.

## Compose demonstration

Start <http://localhost:3000>, submit an assigned job as its child, sign in as
an adult to approve or reject it, then return as the child to inspect state,
balance, and history. Restart Compose to demonstrate persistence.

## Unresolved decisions

Before Phase 3 is complete, an owning issue must define reviewer attribution,
approval-time point override rules, and whether a separate cross-date approval
queue is required. These are planned behavior, not properties of the current
schema.
