# Jobs and agenda

## Status

Retrospective specification of the delivered Phase 2 slices. The phase remains
in progress because job edit/cancel/delete and the adult child filter are not
implemented.

## Outcome and user value

An adult can schedule a dated once-off job for one or more active children. A
family member can browse a household-local day; children see only their own
jobs and can submit open work for adult review.

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Read a daily board | No | Own jobs | All active children's jobs |
| Create once-off jobs | No | No | Yes |
| Submit completion | No | Own open job only | No |

The authenticated session supplies the viewer identity. Clients cannot select
another viewer through request data.

## Scope

Delivered scope includes household-local date browsing, agenda grouping,
once-off creation, atomic multi-child assignment, and completion submission.
Editing, cancelling, deleting, filtering the adult view by child, and a
separate pending-approval page are outside the implemented slice.

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

## Data and migration

The `jobs` table stores the child, job details, household-local scheduled date,
optional time, agenda period, workflow state, and UTC completion/approval
instants. Foreign keys preserve member history. Multi-child copies commit
atomically. Existing migrations are forward-only.

## HTTP contract

- `GET /api/today?date=YYYY-MM-DD` returns viewer, active members, selected and
  current dates, visible jobs, points summary where applicable, and pending
  approval count.
- `POST /api/today/jobs` accepts `childIds`, name, description, points,
  `scheduledDate`, agenda period, and optional time; it returns the created
  child-specific jobs.
- `POST /api/jobs/{id}/complete` transitions the authenticated child's job.

Validation returns Problem Details with field errors. Authentication failures
are `401`, role/ownership failures `403`, missing records `404`, invalid state
transitions `409`, and an unavailable board `503`.

## UI states

The Today route has loading, empty, populated, action-in-progress, validation,
and server-error states. Adults reveal job tools on demand and select one or
more active children. Previous day, next day, and Today controls update the URL
date. Jobs are grouped by agenda period. Phone and tablet journeys use native,
labelled controls and touch-sized actions.

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
- Given a child completes an open owned job, then it becomes pending approval
  with one completion instant; a repeat does not create another submission.
- Given invalid children, a past date, or invalid text/points, then no job copy
  is persisted and field validation is returned.

## Automated tests

Domain tests cover job transitions. Application tests cover visibility,
ownership, validation, multi-child creation, and persistence orchestration.
Integration tests cover HTTP authorization and PostgreSQL behavior. Component
and Playwright tests cover loading/errors, daily navigation, creation,
multi-child assignment, and completion at phone/tablet sizes.

## Compose demonstration

Start the stack with demo data, sign in as an adult at
<http://localhost:3000>, schedule a job for a selected date, then sign in as
the assigned child and submit it. Restart the stack and confirm the job and
state persist.

## Unresolved decisions

Job edit/cancel/delete semantics and their historical effects require a future
issue before implementation. Adult child filtering is tracked separately. The
owning issues must resolve those rules before Phase 2 is marked complete.
