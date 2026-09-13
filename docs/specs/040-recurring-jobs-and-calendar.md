# Recurring jobs and calendar

## Status

Retrospective specification of delivered recurrence-creation slices. Phase 4
remains in progress: recurring-series lifecycle and adult calendar views are
not implemented.

## Outcome and user value

An adult can create daily, weekly, or monthly schedules for one or more active
children. Each child receives an independent series whose occurrences appear
on the same daily agenda and follow the same completion/review workflow as
once-off jobs.

## Actors and authorization

Anonymous and child callers cannot create recurrence. Adults may create series
for active children. Authenticated family members read generated occurrences
through the daily-board visibility rules in `020-jobs-and-agenda.md`.

## Scope

Delivered scope includes daily recurrence, weekly weekday selection, monthly
day-of-month selection, optional end date, atomic multi-child creation,
idempotent request IDs, an eight-week rolling materialization horizon, and
on-demand extension when a later date is browsed. Pause/end/edit, scoped edits,
weekend templates, and day/week/month calendar screens are not implemented.

## Domain rules and state

Each selected child receives a separate series with a shared assignment request
ID. Start date cannot be in the past; end cannot precede start. Weekly series
require one or more distinct weekdays. Monthly day is 1–31; shorter months use
their final valid day. Common job name, description, points, agenda period, and
time validation matches once-off jobs.

Creation materializes occurrences through household today plus 55 days. Reading
a date beyond that advances active series through the requested date. A series'
`GeneratedThrough` watermark and the unique series/date occurrence constraint
prevent duplicates. Reusing a request ID with identical data returns the prior
result; different data returns `409`.

## Data and migration

`recurring_job_series` stores child, creator adult, shared request ID, details,
frequency, weekday mask or monthly day, start/end, and generated-through date.
Generated jobs reference their series and recurrence frequency. Series and all
initial child assignments commit atomically. Migrations are forward-only.

## HTTP contract

- `POST /api/recurring-jobs/daily`
- `POST /api/recurring-jobs/weekly`
- `POST /api/recurring-jobs/monthly`

Each request includes `requestId`, `childIds`, shared job details, start and
optional end. Weekly requests include `weekdays`; monthly requests include
`dayOfMonth`. New requests return `201`; identical retries return `200` with
the existing assignments; conflicting retries return `409`; invalid data
returns validation Problem Details.

## UI states

Adult tools provide frequency-specific forms, labelled multi-child selection,
date fields, weekday controls, and monthly-day input. Successful creation
refreshes the selected daily board. Validation and server errors preserve the
entered form. Generated occurrences carry a recurrence label. No calendar or
series-management UI exists yet.

## Audit and security

Adult authorization is enforced by the API and the creator adult ID is stored
on each series. There is no general audit event or edit/pause history because
those operations are not implemented.

## Observability and health

Occurrence generation uses the normal request transaction and PostgreSQL
constraints. The standard API readiness check covers its database dependency;
no background scheduler or additional service is introduced.

## Acceptance scenarios

- Given valid daily, weekly, or monthly input, when an adult creates a schedule,
  then one series per selected child and its horizon occurrences commit.
- Given the same request ID and data is retried, then the existing assignment is
  returned without duplicate series or jobs.
- Given day 31 in a shorter month, then the occurrence falls on that month's
  final valid day.
- Given a family member browses beyond the watermark, then occurrences generate
  through that date once and remain stable after restart.

## Automated tests

Domain tests cover recurrence validation, weekday masks, monthly month-end
behavior, horizons, and duplicate generation. Application/integration tests
cover authorization, atomic multi-child creation, idempotency/conflicts, and
PostgreSQL uniqueness. Component and Playwright tests cover each creation form,
errors, phone/tablet layouts, and agenda display.

## Compose demonstration

Start <http://localhost:3000>, sign in as an adult, create each recurrence type,
and browse matching dates on the daily board. Retry one unchanged request and
restart the stack to confirm no duplicate occurrences.

## Unresolved decisions

Future issues must define pause/end/edit scope, effects on generated and
approved history, weekend-specific authoring, and the adult calendar's day,
week, and month behavior before those capabilities are implemented.
