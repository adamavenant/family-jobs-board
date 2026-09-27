# Recurring jobs and calendar

## Status

Retrospective specification of delivered recurrence-creation slices, extended
by issue #85 for adult calendar views and issue #108 for scoped recurring-job
edits and cancellations. Phase 4 remains in progress: pause/resume and broader
series administration remain; see issue #107.

## Outcome and user value

An adult can create daily, weekly, or monthly schedules for one or more active
children. Each child receives an independent series whose occurrences appear
on the same daily agenda and follow the same completion/review workflow as
once-off jobs. Alternatively, two or more children can take turns: one series
assigns each occurrence to the next child in an ordered rotation.

## Actors and authorization

Anonymous and child callers cannot create recurrence. Adults may create series
for active children. Authenticated family members read generated occurrences
through the daily-board visibility rules in `020-jobs-and-agenda.md`.

## Scope

Delivered scope includes daily recurrence, weekly weekday selection, monthly
day-of-month selection, optional end date, atomic multi-child creation,
take-turns (round-robin) schedules,
idempotent request IDs, an eight-week rolling materialization horizon, and
on-demand extension when a later date is browsed, and adult day/week/month
calendar views (issue #85), plus scoped edits and terminal cancellation from an
occurrence (issue #108). Pause/resume and weekend-specific authoring templates
are not implemented (issue #107); weekdays can already be expressed by selecting
Monday through Friday in a weekly schedule.

## Domain rules and state

Each selected child receives a separate series with a shared assignment request
ID. Start date cannot be in the past; end cannot precede start. Weekly series
require one or more distinct weekdays. Monthly day is 1–31; shorter months use
their final valid day. Common job name, description, points, agenda period, and
time validation matches once-off jobs.

With `takeTurns`, the selected children form an ordered rotation of at least
two distinct active children and a single series is created, owned by the
first-turn child. Occurrence *n* (0-based, across all generated occurrences)
belongs to `rotation[n mod size]`. The series persists its next-turn index so
incremental generation continues the cycle. Turns advance per occurrence even
if one is later cancelled or rejected. Editing a rotation, skipping a turn, and
removing deactivated children from a rotation are not implemented.

Creation materializes occurrences through household today plus 55 days. Reading
a date beyond that advances active series through the requested date. A series'
`GeneratedThrough` watermark and the unique series/date occurrence constraint
prevent duplicates. Reusing a request ID with identical data returns the prior
result; different data returns `409`.

An adult editing or cancelling a recurring occurrence chooses `This Only`, `All
Future`, or `All`. `This Only` changes only the selected occurrence. `All Future`
uses that occurrence's original scheduled date as an inclusive boundary. `All`
reconciles every stored occurrence in the series and applies the new pattern to
future generation. `Open` and `PendingApproval` occurrences are eligible;
`Approved` and `Cancelled` occurrences are immutable. Reconciliation can update,
cancel, or create occurrences, but it never creates a newly scheduled occurrence
before household today and never changes a points-ledger entry.

Point increases are not applied retrospectively to occurrences before household
today; those occurrences retain their existing points and the preview reports
the skip. Point reductions may apply to eligible past occurrences. An occurrence
may be moved to a date after its recorded completion time, with a warning shown
before confirmation. Pattern changes may remove an existing eligible occurrence,
which terminally cancels it. Series changes use a version supplied by the preview
so stale confirmations fail with `409` rather than overwriting a newer change.

## Data and migration

`recurring_job_series` stores child, creator adult, shared request ID, details,
frequency, weekday mask or monthly day, start/end, generated-through date, a
concurrency version, and
for take-turns schedules an ordered `rotation_child_ids` array and
`next_turn_index` (empty and 0 otherwise; a check constraint keeps them
consistent with `child_id`).
Generated jobs reference their series and recurrence frequency and retain their
original recurrence-slot date when a single occurrence is moved. Series and all
initial child assignments commit atomically. Migrations are forward-only.
`recurring_job_series_revisions` records the effective schedule snapshot for each
broad change. `recurring_job_changes` records the request ID, scope, operation,
input fingerprint, actor, outcome counts, warnings, and resulting series version,
providing idempotent retries and an audit trail.

## HTTP contract

- `POST /api/recurring-jobs/daily`
- `POST /api/recurring-jobs/weekly`
- `POST /api/recurring-jobs/monthly`
- `POST /api/jobs/{jobId}/recurring-change/preview`
- `POST /api/jobs/{jobId}/recurring-change`

Each request includes `requestId`, `childIds`, shared job details, start and
optional end. Weekly requests include `weekdays`; monthly requests include
`dayOfMonth`. Optional `assignmentMode` is `eachChild` (default) or
`takeTurns`; with `takeTurns`, `childIds` order is the rotation order and the
response holds one assignment whose `rotationChildIds` echoes it. New requests
return `201`; identical retries return `200` with
the existing assignments; conflicting retries return `409`; invalid data
returns validation Problem Details.

The recurring-change routes are adult-only. One preview request returns the
series version and the impact for each valid scope, including the number of jobs
that would be updated, cancelled, created, or skipped and any warnings. A scope
that is invalid for the proposed values carries its own explanation without
blocking the other choices. Apply requires a request ID and the previewed
version, executes the full change atomically, and returns the selected impact.
Reusing the request ID with the same request returns the stored result; reusing
it with different data, using a stale version, or moving a single occurrence
onto an occupied date returns `409`. A child caller receives `403`.

- `GET /api/calendar?view={day|week|month}&date={yyyy-MM-dd}&childId={guid}` —
  adult-only. `view` and `date` default to the week containing household today
  when omitted. Returns the same `viewer`/`members` shape as `/api/today`, plus
  `anchorDate`, `rangeStart`, `rangeEnd`, `selectedChildId`, and one
  `CalendarDayResponse` per date in range (`date`, `isInFocusedPeriod`, and
  `jobs` using the exact same `JobResponse` type `/api/today` returns for that
  date — the two endpoints share their mapping code, so they cannot disagree).
  An unrecognized `view` or an unknown/inactive `childId` returns a validation
  Problem Details; a child caller gets `403`.

## UI states

Adult tools provide frequency-specific forms, labelled multi-child selection,
date fields, weekday controls, and monthly-day input. With two or more
children in the household, adults choose "Each child does it" or "Take turns";
taking turns reveals a "First turn" choice, and the other selected children
follow in household order. Successful creation
refreshes the selected daily board. Validation and server errors preserve the
entered form. Generated occurrences carry a recurrence label.

A separate, bookmarkable `/calendar` page (linked from the daily board's header
for adults only) offers day/week/month views with the same child filter as the
daily agenda. Week starts on Monday; month view is always a fixed 42-day (six
week) grid, with days outside the focused month visually de-emphasized, so the
grid's shape never changes between months. The calendar is read-only: each job
links back to the daily agenda at its date for completing, approving, or
rejecting it, so only one page is ever responsible for state-changing actions.
A job whose scheduled date has passed while it is still `Open` is marked
"overdue" in the calendar, directly demonstrating that it remains visible on
its originally assigned date rather than silently disappearing or moving.
No series-management UI exists yet.

On the daily agenda, editing or cancelling a recurring occurrence first collects
the requested change and then presents three clearly described choices: `This
Only`, `All Future`, and `All`. Each choice shows the server-calculated impact
and warnings before confirmation. Once confirmed, the agenda refreshes and shows
the applied counts. Once-off jobs retain their existing edit/cancel experience.

## Audit and security

Adult authorization is enforced by the API and the creator adult ID is stored
on each series. Every applied recurring change stores its adult actor, stable
request ID, input fingerprint, scope, operation, outcome, and time; every broad
edit stores the resulting schedule revision. Approved history remains immutable.

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
- Given a take-turns schedule for two children, then consecutive occurrences
  alternate between them, including occurrences generated later by browsing
  beyond the watermark after a restart; three or more children rotate round
  robin.
- Given an adult opens the calendar, then they can switch between day, week,
  and month views and the shown jobs exactly match the daily agenda for the
  same date.
- Given a job on a past date that was never completed, when it appears on the
  calendar, then it still shows on its assigned date, marked overdue.
- Given a child, when they request the calendar, then the API returns `403`.
- Given an adult changes a daily series to weekdays, when they choose `All
  Future`, then eligible generated weekend occurrences on or after the selected
  date are cancelled and missing weekday occurrences are created only for today
  or later.
- Given an approved or already cancelled occurrence is in scope, when a broad
  edit or cancellation is applied, then it is skipped and its history and ledger
  remain unchanged.
- Given a point increase covers an eligible past occurrence, when the edit is
  applied, then its points remain unchanged and the result reports the skip.
- Given the same change request is retried, then the stored result is returned
  without applying the change twice; a stale series version returns `409`.
- Given a single occurrence is moved beyond the generated horizon, when later
  browsing extends generation past that date, then neither its original slot nor
  its moved date is recreated.
- Given a child, when they preview or apply a recurring change, then the API
  returns `403`.

## Automated tests

Domain tests cover recurrence validation, weekday masks, monthly month-end
behavior, horizons, and duplicate generation. Application/integration tests
cover authorization, atomic multi-child creation, idempotency/conflicts, and
PostgreSQL uniqueness.

Calendar-specific: Application tests cover day/week/month range computation
(Monday week start, the fixed 42-day month grid, focused-vs-adjacent-month
marking, a leap-year February), child filtering, generation extending to cover
the visible range, and request validation. PostgreSQL integration tests prove
the calendar and the daily agenda return identical job state for the same
date across day and week views, cover a real recurring series generating
correctly across a multi-week range, an overdue job seeded directly (since the
once-off creation endpoint itself rejects a past date), and authorization.
Vitest tests cover switching views, the child filter, the overdue marker, and
that only adults see the calendar link.

Daylight saving isn't separately exercised for the calendar: its range
computation is pure `DateOnly` arithmetic with no time-of-day or UTC-offset
component, so it is unaffected by DST regardless of household time zone.
Component and Playwright tests cover each creation form, errors, phone/tablet
layouts, and agenda display.

Scoped-change application tests cover weekday reconciliation, immutable history,
non-retrospective point increases, take-turn cursor recalculation, idempotency,
and terminal cancellation. PostgreSQL integration tests cover preview and apply,
the persisted revision/change audit, retry stability, and preservation of an
approved ledger award. Vitest tests cover the three choices and edit/cancel
confirmation flow.

## Compose demonstration

Start <http://localhost:3000>, sign in as an adult, create each recurrence type,
and browse matching dates on the daily board. Retry one unchanged request and
restart the stack to confirm no duplicate occurrences.

## Unresolved decisions

Pause/resume, changing the take-turn rotation membership, and dedicated weekend
authoring remain future work (issue #107). The calendar itself is read-only by design;
adding actions directly on it (rather than linking to the daily agenda) is not
planned but could be revisited if that link-out proves inconvenient in
practice.
