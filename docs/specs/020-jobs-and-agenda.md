# Jobs and agenda

## Status

Retrospective specification of the delivered Phase 2 slices.

## Outcome and user value

An adult can schedule a dated once-off job for one or more active children. A
family member can browse a household-local day; children see only their own
jobs and can submit open work for adult review.

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Read a daily board | No | Own jobs; cannot filter | All active children's jobs, optionally filtered to one active child |
| Create once-off jobs | No | No | Yes |
| Edit open or pending-approval jobs | No | No | Yes |
| Cancel open or pending-approval jobs | No | No | Yes |
| Submit completion | No | Own open job only | No |

The authenticated session supplies the viewer identity. Clients cannot select
another viewer through request data.

## Scope

Delivered scope includes household-local date browsing, agenda grouping,
adult filtering by one active child, once-off creation, atomic multi-child
assignment, occurrence editing, terminal cancellation, and completion
submission. Recurring-series lifecycle operations and a separate
pending-approval page are outside the implemented slice.

## Domain rules and state

A job belongs to exactly one child. A multi-child request creates one
independent job per selected active child in one transaction. Name is required
and trimmed (maximum 160 characters); description is trimmed (maximum 1,000);
points are a non-negative whole number. Scheduled dates cannot be in the past.
Agenda period is `morning`, `arrivingHome`, `evening`, or `unscheduled`, with
an optional time.

Jobs start `Open`. Their child may transition them once to `PendingApproval`,
capturing a UTC completion instant. Another child receives `403`; a missing job
receives `404`; and an invalid repeat transition receives `409`.

An adult may edit all job details while a job is `Open` or `PendingApproval`.
Editing retains its workflow state, completion and review history, recurring
series link, and creates no ledger entry. An adult may cancel either state with
an optional reason. Cancellation records actor and UTC time, transitions the
job to terminal `Cancelled`, hides it from every daily agenda, and never awards
points. `Approved` and `Cancelled` jobs are immutable. Cancelling one recurring
occurrence does not change its series or future occurrences; there is no
un-cancel operation.

## Data and migration

The `jobs` table stores the child, job details, household-local scheduled date,
optional time, agenda period, workflow state, and UTC completion/approval
instants. Cancellation additionally stores the adult actor, UTC instant, and
optional reason. Foreign keys preserve member history. Multi-child copies
commit atomically. Existing migrations are forward-only.

## HTTP contract

- `GET /api/today?date=YYYY-MM-DD&childId=UUID` returns viewer, active members,
  selected and current dates, the selected child when present, visible jobs,
  points summary where applicable, and the household pending-approval count.
  Only adults may supply `childId`, and it must identify an active child in the
  household.
- `POST /api/today/jobs` accepts `childIds`, name, description, points,
  `scheduledDate`, agenda period, and optional time; it returns the created
  child-specific jobs.
- `POST /api/jobs/{id}/complete` transitions the authenticated child's job.
- `PUT /api/jobs/{id}` lets an adult replace the editable details of one open
  or pending-approval occurrence.
- `POST /api/jobs/{id}/cancel` lets an adult terminally cancel one open or
  pending-approval occurrence with an optional reason.

Validation returns Problem Details with field errors. Authentication failures
are `401`, role/ownership failures `403`, missing records `404`, invalid state
transitions `409`, and an unavailable board `503`.

## UI states

The Today route has loading, empty, populated, action-in-progress, validation,
and server-error states. Adults reveal job tools on demand, select one or more
active children for assignment, and filter the agenda by child. Previous day,
next day, and Today controls preserve the filter in the URL while updating the
date. The heading, count, jobs, and empty state reflect the selected child.
Jobs are grouped by agenda period. Phone and tablet journeys use native,
labelled controls and touch-sized actions. Each eligible adult job card exposes
on-demand edit and cancellation forms; children never receive those controls.

## Audit and security

The API, not control visibility, enforces adult creation and child ownership.
The delivered job records do not yet capture creating actor or a general audit
event; that remains part of administration/audit completeness.

## Observability and health

The slice adds no separate health endpoint. Failures flow through API Problem
Details and route error handling; the standard database readiness check covers
its persistence dependency.

## Acceptance scenarios

- Given an adult selects two active children, when one dated job is submitted,
  then two independent jobs are committed and appear on that date.
- Given a child views a date, then only that child's jobs are returned.
- Given an adult selects an active child, then the agenda shows only that
  child's jobs while the household pending-approval count remains unchanged.
- Given a child attempts to use the child filter, then the API returns `403`;
  unknown, inactive, and adult member selections return field validation.
- Given a child completes an open owned job, then it becomes pending approval
  with one completion instant; a repeat does not create another submission.
- Given an adult edits an open or pending-approval job, then its details update
  without changing workflow/review state or creating a ledger entry.
- Given an adult cancels an open or pending-approval job, then it is retained as
  `Cancelled`, disappears from every agenda, retains review history, and awards
  no points.
- Given an approved job, child caller, or already-cancelled job, then edit and
  cancellation are rejected.
- Given one recurring occurrence is cancelled, then later series occurrences
  remain unchanged.
- Given invalid children, a past date, or invalid text/points, then no job copy
  is persisted and field validation is returned.

## Automated tests

Domain tests cover job transitions. Application tests cover visibility,
ownership, validation, multi-child creation, and persistence orchestration.
Integration tests cover HTTP authorization and PostgreSQL behavior. Component
and Playwright tests cover loading/errors, daily navigation, adult child
filtering, creation, multi-child assignment, editing, cancellation, and
completion at phone/tablet sizes.

## Compose demonstration

Start the stack with demo data, sign in as an adult at
<http://localhost:3000>, schedule a job for a selected date, then sign in as
the assigned child and submit it. Restart the stack and confirm the job and
state persist.

## Unresolved decisions

Recurring-series edit, pause, and end semantics remain owned by the recurring
jobs specification. Cancellation is the terminal retained-row behavior for
once-off jobs; no separate physical-delete workflow is planned for Phase 2.
