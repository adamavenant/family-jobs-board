# Whose Turn Is It? household rotations

## Status

Daily rotations were implemented by issue #112; tick-to-advance rotations, for
turns that don't happen every day, were added by issue #135 (see
[Tick-to-advance rotations](#tick-to-advance-rotations)). This spec was written
alongside the implementation, following the precedent set by
`050-good-behaviours.md` and `060-redemptions-adjustments-and-audit.md`: it is
not tied to one of the `PLAN.md` MVP phases, so it is recorded here rather
than forced into that table.

## Outcome and user value

The daily board answers "Who is Pink today?" — the household practice where
one child gets the pink crockery each day. An adult configures one or more
rotations — for example "Who is Pink today?" and "Who sits next to Mum?" —
each with its own ordered list of participating children; from then on every household-local
calendar day automatically advances to the next child and wraps at the end,
with no daily data entry and no background job. The feature is purely
informational: it is independent of jobs, completions, approvals, and points,
and distinct from the take-turns rotation used for shared recurring jobs
(#111).

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| See today's (or a browsed date's) answer on the board | No | Yes | Yes |
| Read the rotation configuration and upcoming preview | No | No | Yes |
| Create a new effective-dated rotation revision | No | No | Yes |

A child who is not a participant still sees the card; the rotation is a
whole-household display, not a per-viewer one. Children never see the
configuration screen, and the API rejects a non-adult on every
`/api/turn-rotation` route.

## Domain rules and state

- A household can run any number of independent rotations. Each has a
  `RotationId`; its history is the set of revisions sharing that ID. Rotations
  never affect one another, and the board shows one card per active rotation in
  the order they were created.
- `TurnRotationRevision` is an immutable, effective-dated configuration:
  `EffectiveFrom` (date), `Question`, an ordered list of distinct child
  participants, `FirstChildId` (which participant is assigned on
  `EffectiveFrom`), the creating adult, and the UTC creation instant.
- The assignee for a date is computed as
  `participants[(indexOf(FirstChildId) + (date − EffectiveFrom in days)) mod participantCount]`.
  Every calendar day counts, including weekends and holidays. A one-child
  rotation always answers with that child.
- The applicable revision for a date is the one with the latest
  `EffectiveFrom` that is on or before that date (ties broken by the most
  recently created revision). A date before every revision's `EffectiveFrom`
  has no applicable revision.
- Nothing is precomputed or cached per day: the answer is derived from the
  configured household time zone and the revision list on every read, so
  restarts or extended downtime never desynchronise it and no nightly job is
  needed.
- Revisions are never edited or deleted. Reconfiguring — including changing
  participants, reordering, changing who goes first, or dropping a
  participant — always creates a new revision. A rotation's first revision may be
  effective today or later; once it has a revision, every later revision of
  that rotation must be effective no earlier than tomorrow (household-local), so today's
  answer, and every date before the new revision's effective date, is
  unaffected. A participant who is dropped from a later revision keeps
  appearing on the dates the earlier revision already governs. When a child is
  deactivated elsewhere in the system, the API automatically creates a
  replacement revision effective tomorrow that leaves them out (the next
  assignee is preserved where possible); if nobody remains, a participant-less
  "cleared" revision makes the rota unavailable from tomorrow until an adult
  configures it again. Today and earlier dates still resolve, including the
  deactivated child's display name. Restoring a child never re-adds them: an
  adult must include them in a new revision.
- A revision must have at least one participant, all distinct, all currently
  active children (validated at save time). `FirstChildId` must be one of the
  selected participants. The question defaults to "Who is Pink today?" when
  left blank and is otherwise trimmed to at most 200 characters.
- An adult can end a rotation: a participant-less "cleared" revision effective
  tomorrow removes it from the board from then on, while today and earlier dates
  keep their answers.
- If no rotation exists, nothing is shown to children: `GET
  /api/today` reports no whose-turn answer and the configuration screen shows
  a setup invitation instead of a saved configuration.

## Data and migration

`AddTurnRotations` adds two tables:

- `turn_rotation_revisions` — `id`, `rotation_id`, `effective_from` (date), `question`,
  `first_child_id` (null for a cleared revision), `created_by_member_id`, `created_at_utc`. Indexed on
  `(rotation_id, effective_from)` for resolving each rotation's applicable
  revision.
- `turn_rotation_participants` — an owned collection keyed by
  `(turn_rotation_revision_id, order_index)`, with `child_id` and a unique
  index on `(turn_rotation_revision_id, child_id)` so a revision cannot repeat
  a participant. `order_index` is an ordinary assigned column, not a database
  identity, because it encodes the configured rotation order.

Both tables reference `household_members` with `ON DELETE RESTRICT`, matching
the soft-delete convention used throughout the schema. There is no rollback
data loss risk beyond the standard forward-only migration contract: `Down`
drops both tables.

## HTTP contract

- `GET /api/today?date=YYYY-MM-DD` — any signed-in member. The existing daily
  board response gains `whoseTurns`: a list of `{ rotationId, question,
  childId, childDisplayName }`, one per rotation that applies to that date
  (empty when none do). This reuses the existing board endpoint so the
  browsed date and the answers always agree.
- `GET /api/turn-rotations` — adult-only. Returns `{ rotations }`; each item
  is `{ rotationId, current, upcomingTurns }`, where `current` is the revision
  applicable today (or `null` if it has not started) and `upcomingTurns` is a
  14-day preview of `{ date, question, childId, childDisplayName }`.
- `POST /api/turn-rotations` — adult-only. Body: `{ participantChildIds,
  firstChildId, effectiveFrom, question }`. Starts a new rotation (effective
  today or later); returns the refreshed `{ rotations }`.
- `PUT /api/turn-rotations/{rotationId}` — adult-only. Same body; adds a new
  revision effective no earlier than tomorrow; `404` for an unknown or ended
  rotation.
- `DELETE /api/turn-rotations/{rotationId}` — adult-only. Ends the rotation
  from tomorrow; `404` if unknown or already ended.
- All writes return `400` with field errors for an empty, duplicate, inactive,
  or non-child participant list, a `firstChildId` outside the selected
  participants, an `effectiveFrom` earlier than allowed, or an over-long
  question. Children receive `403` from every `turn-rotations` route.

## UI

- One compact card per rotation sits directly under the daily board's date heading. On
  today it reads the configured question verbatim (default "Who is Pink
  today?"); browsing another date rewrites a trailing "today" to "on
  {Weekday}" (for example "Who is Pink on Friday?") so custom questions still
  read naturally. The child's display name is shown next to a small
  initial-letter badge as its visual identity — the codebase has no existing
  per-child avatar/colour system, so this badge is the visual identity this
  feature introduces rather than a new persistent per-child attribute.
- A daily rotation's card is informational only: no completion action, no
  interaction. A tick-to-advance rotation's card adds a tick control on today
  (see below).
- Children never see an unconfigured card. Adults instead see a small
  "isn't set up yet" note near the same spot, pointing at the admin tools.
- The adult "Whose Turn Is It?" tool lists the existing rotations (each with
  today's answer, an "Edit" form, and an "End" action with confirmation) and an
  "Add a rotation" form; the tool (in the same collapsible tools list as
  Good behaviours, Points, and Family administration) offers: a checkbox per
  active child, an up/down-button reorderable list of the selected children,
  a "first turn" picker restricted to the selected children, an effective
  date input (its minimum enforced client-side and re-validated
  server-side), an optional question field, and — once configured — a
  14-day upcoming-turns preview.

## Audit and security

Every revision records its creating adult and UTC creation instant, matching
the audit convention used for good-behaviour types and point adjustments.
Because revisions are immutable, this is a complete history: there is
nothing to correct in place, only a new revision to supersede an old one.
Authorization is enforced server-side (`RequireAuthorization("Adult")` on
both `turn-rotation` routes), not only hidden in the UI.

## Observability and health impact

No new background work, scheduled job, or external dependency. The feature
adds two indexed tables and one additional read (`TurnRotationService`) inside
the existing `/api/today` request; it does not change health check or
readiness behaviour.

## Acceptance criteria

- Given an adult adds a second rotation (for example "Who sits next to Mum?")
  alongside "Who is Pink today?", the board shows a card for each and each
  advances independently; ending one leaves the other running.
- Given three children ordered A, B, C with A first on Monday, the board
  shows B on Tuesday, C on Wednesday, and A on Thursday.
- Given one participating child, that child is shown every day.
- Given the application was offline for several days, reopening it shows the
  correct child for the current household-local date without any catch-up
  job.
- Given a family member browses another date, the board shows that date's
  child and date-appropriate question wording.
- Given an adult schedules a revised rotation for tomorrow, today remains
  unchanged and tomorrow starts the new revision with the selected child.
- Adding, removing, reordering, or dropping children from the rotation does
  not rewrite earlier dates' answers.
- A child cannot configure the rotation; invalid, duplicate, inactive, or
  non-child participants save no revision and return field validation.
- `GET /api/today?date=YYYY-MM-DD` exposes the applicable question and
  child, or `null` when no revision applies.
- Adult-only read/update configuration endpoints support an upcoming-turns
  preview and create effective-dated revisions.
- A forward-only EF migration persists revisions and ordered participants,
  including creator and UTC creation time.

## Tests

- Domain (`TurnRotationRevisionTests`): rotation arithmetic for two, three,
  and one-child rotations; the first-child starting offset; stability across
  a leap day; question defaulting/trimming; and every construction-time
  validation rule.
- Application (`TurnRotationServiceTests`): resolving the applicable revision
  per date, a revision scheduled for tomorrow leaving today unchanged,
  reconfiguration never rewriting dates before the new revision, non-adult
  rejection, every field-validation case (no/duplicate/inactive/non-child
  participants, an out-of-set first child, an over-early effective date), and
  the upcoming-turns preview.
- PostgreSQL integration (`TurnRotationEndpointsTests`): the unconfigured
  board, board + configuration round-trips through real HTTP and the
  `/api/today` endpoint, two-child alternation and one-child stability across
  dates, the "schedule for tomorrow" scenario end-to-end, the overview and
  preview endpoint, child authorization rejection on both routes, every
  validation case, and the "same-day second save must wait until tomorrow"
  rule.
- Vitest/Testing Library (`WhoseTurn.test.tsx`): the card renders today's
  answer; the unset invitation shows to adults and not to children; the
  admin form lets an adult pick participants, reorder them, choose the first
  turn, and save, with the resulting request body and the upcoming preview
  asserted; submit is disabled with no participants selected.
- Household-midnight and DST boundaries are covered by the existing
  `IHouseholdClock`/`SystemHouseholdClock` contract (household-local
  `DateOnly` computed from a `TimeZoneInfo` conversion), which every other
  date-based feature already relies on; the rotation's own arithmetic is
  plain `DateOnly` day-number subtraction, which is unaffected by DST since
  it never re-derives a wall-clock time from the difference.

## Compose demonstration

1. `docker compose up --build` (or `./scripts/compose-up.sh`).
2. Bootstrap the first adult, then add two children under "Manage family".
3. Open "Whose Turn Is It?", select both children, choose an order and a
   first turn, leave the effective date at today, and save.
4. The board now shows the card immediately under the date heading; use
   "Next day →" to see the child alternate and the question reword to "on
   {Weekday}".
5. Reopen the tool and save a second revision effective tomorrow with a
   different setup — today's card is unchanged, and the upcoming preview
   shows the new configuration starting tomorrow.

## Tick-to-advance rotations

Issue #135 adds a second advance mode for turns that don't happen every day,
such as "Who is first in the shower?" when showers are every other day. The
rotation only moves on when the current child's turn is ticked off on the
board.

### Domain rules

- Every revision has an `AdvanceMode`: `Daily` (the default; every revision
  saved before #135 migrates as `Daily`) or `WhenTicked`. Switching mode is an
  ordinary reconfiguration: a new revision, effective tomorrow at the
  earliest.
- A `TakenTurn` (a tick) records the rotation, the revision it counts towards,
  the household-local date, the child whose turn was taken, the recording
  member, and the UTC time. Ticks are append-only: unticking records who and
  when, and an unticked tick no longer counts.
- In a `WhenTicked` revision the turn starts with `FirstChildId` on
  `EffectiveFrom` and moves to the next participant once per tick of that
  revision; days without a tick do not advance it. Ticks never carry over to
  a later revision, which restarts from its own first child.
- What a tick does depends on the applicable revision's participant count:
  - **Two children — moves on immediately.** Today's card switches straight
    to the other child, who can then be ticked the same day.
  - **One, or three or more — done today, next tomorrow.** Today's card shows
    the ticked child as done; the next child is up from the following day.
    The rotation can be ticked at most once a day.
- For a date, the answer is whose turn it was at the start of that day (the
  first child advanced by the ticks on earlier dates) and whether a turn was
  ticked that day — except that today in a two-child rotation already counts
  today's ticks. A future date therefore shows the child whose turn it is now.
- Every answer carries a turn number within its revision, counting from zero:
  the days since the effective date in a daily rotation, or the turns ticked
  off before it in a tick-to-advance rotation. A revision and a turn number
  identify one turn, even when the same child's turn comes round again.
- Only today (household-local) can be ticked, by any adult or by the child
  whose turn it is. The tick names the turn the board showed — revision, turn
  number, and child — so a stale tick (double tap, two devices, or a request
  for a child's earlier turn once theirs has come round again) is refused
  rather than moving the rotation on twice. It also carries a client-generated
  request ID: replaying a request returns the original tick without ticking
  again, and reusing an ID for a different tick is a conflict. Ticks and
  unticks for a rotation are serialized by a row lock on its revisions.
- The most recent tick can be unticked on the day it was made, by any adult or
  by the child whose turn was ticked, restoring the previous answer. Older
  ticks, and ticks from earlier days, cannot be unticked.
- Deactivating a participant still creates a replacement revision from
  tomorrow. It keeps the rotation's mode and starts from the child projected
  to be next once ticks are counted.

### Data and migration

`AddTickedTurnRotations` adds `advance_mode` (`Daily` | `WhenTicked`,
default `Daily` for existing rows) to `turn_rotation_revisions`, and a
`turn_rotation_taken_turns` table: `id`, `request_id`, `rotation_id`,
`turn_rotation_revision_id`, `turn_number`, `turn_date`, `child_id`,
`recorded_by_member_id`, `recorded_at_utc`, `undone_by_member_id`, and
`undone_at_utc`. It is indexed on `(turn_rotation_revision_id, turn_date)`,
has a unique index on `request_id` and a unique index on
`(turn_rotation_revision_id, turn_number)` for ticks that have not been
unticked (so each turn is taken at most once, backing up the row lock),
references revisions and `household_members` with `ON DELETE RESTRICT`, and
a check constraint keeps the unticking member and time set together. `Down`
drops the table and the column.

### HTTP contract

- `SaveTurnRotationRequest` and the configuration response gain
  `advanceMode`: `"daily"` or `"whenTicked"` (camelCase, like the other
  string-valued fields). Omitted or `null` means `"daily"`; anything else is a
  `400` field error.
- Each rotation in `GET /api/turn-rotations` gains `upNext`: for a
  tick-to-advance rotation, the participants in turn order starting with
  whoever takes the next turn (empty for a daily rotation).
  `upcomingTurns` leaves out future dates governed by a tick-to-advance
  revision, since they can't be predicted.
- Each `whoseTurns[]` item in `GET /api/today` gains `revisionId` and
  `turnNumber` (identifying the turn shown), `advanceMode`, `turnTaken`,
  `canTakeTurn` (whether this viewer may tick it today), and `undoableTurn`
  (`{ turnId, childId, childDisplayName }` when this viewer may untick today's
  most recent tick, else `null`).
- `POST /api/turn-rotations/{rotationId}/turns` — any signed-in member, body
  `{ requestId, revisionId, turnNumber, childId }` copied from the board's
  answer, with a fresh `requestId` per tick. Returns the `TakenTurnResponse`
  (including its `requestId`, `revisionId`, and `turnNumber`); replaying a
  `requestId` with the same body returns the original tick. `400` for a daily
  rotation or a missing field; `403` unless the caller is an adult or that
  child; `404` for an unknown or ended rotation; `409` if the rotation hasn't
  started, the named turn is no longer the current one, today is already
  ticked, or the `requestId` was used for a different tick.
- `DELETE /api/turn-rotations/{rotationId}/turns/{turnId}` — any signed-in
  member. Returns the unticked `TakenTurnResponse`. `403` unless the caller is
  an adult or the child whose turn was ticked; `404` for an unknown tick or
  one from another rotation; `409` if it is already unticked, not from today,
  or not the most recent tick.

### UI

- The adult rotation form gains a **Moves on** choice — "Every day" or "When
  a turn is ticked off" — with helper text explaining the two-child and
  three-or-more behaviour. Editing a tick-to-advance rotation pre-selects the
  child who is up next as the first turn, so saving carries on from them; the
  default follows the latest overview (for example after a tick on the board
  while the form is mounted) until the adult picks a first turn themselves.
- A tick-to-advance rotation shows an "Up next" order instead of the 14-day
  preview, and its list entry notes that it moves on when ticked.
- On today's board card, a tick-to-advance rotation offers a native, labelled
  checkbox ("{Child} has had their turn") to viewers who may tick it. With one
  or three or more children the ticked card shows "✓ had their turn" and the
  same checkbox unticks it; with two children the card moves on and offers
  "Undo {Child}’s turn" to whoever may untick it. A refused tick is explained
  inline and the board reloads to show the latest turn. Other dates, and
  daily rotations, show no tick control.
- Verified at phone (375px) and tablet (768px) widths without sideways
  scrolling.

### Acceptance criteria

- An adult can create or edit a rotation choosing "Every day" or "When a turn
  is ticked off"; existing rotations stay daily with unchanged answers.
- Given three children A, B, C with A first in a tick-to-advance rotation,
  days without a tick keep showing A; once A's turn is ticked on Monday,
  Monday shows A as done and Tuesday onward shows B until B's turn is ticked.
- With three or more children a second tick on the same day is rejected.
- Given two children A and B, ticking A's turn shows B immediately, and
  ticking B's turn the same day shows A.
- Any adult or the child whose turn it is can tick; any other child gets
  `403` and sees no tick control.
- The latest tick can be unticked the same day by an adult or the child whose
  turn was ticked, restoring the previous answer; older ticks and ticks from
  earlier days cannot be unticked; unticks keep who and when.
- A stale or duplicate tick returns `409` and does not advance the rotation
  twice.
- Only today can be ticked; past dates show the turn and whether it was
  ticked; future dates show the projected child without a tick control.
- Reconfiguring a tick-to-advance rotation or deactivating one of its
  children never changes earlier dates' answers, and the edit form
  pre-selects the projected next child.

### Tests

- Domain (`TickedTurnRotationTests`): the done-today and moves-on-immediately
  arithmetic for one, two, and three children; past and future dates; the
  once-a-day and stale-tick rules; unticking; ticks from another revision;
  daily rotations ignoring ticks.
- Application (`TurnRotationServiceTests`): saving and validating the mode,
  the overview's `upNext` and trimmed preview, tick/untick permissions and
  conflicts, ticks saved while holding the rotation lock, past dates without
  tick controls, and deactivation preserving the mode and next turn.
- PostgreSQL integration (`TurnRotationEndpointsTests`): the migration
  defaulting existing revisions to daily, both behaviours end to end over
  HTTP, permissions and conflicts, unticks persisted with who, simultaneous
  ticks moving the rotation on only once, and A → B → a replay of A's
  original request returning the original tick while a fresh request for that
  stale turn is a conflict.
- Vitest (`WhoseTurn.test.tsx`): ticking, unticking, the two-child undo, no
  controls for other children, other dates, or daily rotations, a refused
  tick, and the form's mode choice, "Up next" preview, and pre-selected next
  child — including an untouched default following a tick made while the
  form was mounted, and a deliberately chosen first turn being kept.
- Playwright (`whoseTurn.spec.ts`): a child ticks and unticks turns in two-
  and three-child rotations at phone and tablet widths, with keyboard ticking
  and no sideways scrolling.

## Out of scope

Per-weekday rotations, skipping or swapping
an individual date, notifications/reminders, and any interaction with jobs,
approvals, or points — all as scoped by issue #112. For tick-to-advance
rotations (#135), ticking a past or future date and choosing the two-child
vs three-or-more behaviour per rotation (it is derived from the participant
count) are also out of scope. Per-child avatars or
colours beyond the single-letter badge introduced here are not a general
identity system and are not implemented elsewhere.
