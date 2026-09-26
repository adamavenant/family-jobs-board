# Whose Turn Is It? daily household rotation

## Status

Implemented by issue #112. This spec was written alongside the implementation,
following the precedent set by `050-good-behaviours.md` and
`060-redemptions-adjustments-and-audit.md`: it is not tied to one of the
`PLAN.md` MVP phases, so it is recorded here rather than forced into that
table.

## Outcome and user value

The daily board answers "Who is Pink today?" — the household practice where
one child gets the pink crockery each day. An adult configures an ordered
list of participating children once; from then on every household-local
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
  participant — always creates a new revision. The first-ever revision may be
  effective today or later; once any revision exists, every later revision
  must be effective no earlier than tomorrow (household-local), so today's
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
- If no revision has ever been created, the rota is unavailable: `GET
  /api/today` reports no whose-turn answer and the configuration screen shows
  a setup invitation instead of a saved configuration.

## Data and migration

`AddTurnRotations` adds two tables:

- `turn_rotation_revisions` — `id`, `effective_from` (date), `question`,
  `first_child_id` (null for a cleared revision), `created_by_member_id`, `created_at_utc`. Indexed on
  `effective_from` for resolving the applicable revision.
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
  board response gains a `whoseTurn` field: `{ question, childId,
  childDisplayName } | null`. `null` means no revision applies to that date,
  or the rota was cleared because every participant was deactivated. This reuses the existing board endpoint rather than adding a
  parallel read path, so the browsed date and the answer always agree.
- `GET /api/turn-rotation` — adult-only. Returns `{ current, upcomingTurns, hasRevisions
  }`: `current` is the revision applicable today (or `null` if unconfigured),
  and `upcomingTurns` is a 14-day preview of `{ date, question, childId }`
  starting today, each day resolved independently so an already-scheduled
  future revision shows correctly ahead of its effective date.
- `PUT /api/turn-rotation` — adult-only. Body: `{ participantChildIds,
  firstChildId, effectiveFrom, question }`. Always creates a new revision;
  `200` with the refreshed `{ current, upcomingTurns }` on success, `400`
  with field errors for an empty, duplicate, inactive, or non-child
  participant list, a `firstChildId` outside the selected participants, an
  `effectiveFrom` earlier than allowed, or an over-long question. Children
  receive `403` from both `turn-rotation` routes.

## UI

- A compact card sits directly under the daily board's date heading. On
  today it reads the configured question verbatim (default "Who is Pink
  today?"); browsing another date rewrites a trailing "today" to "on
  {Weekday}" (for example "Who is Pink on Friday?") so custom questions still
  read naturally. The child's display name is shown next to a small
  initial-letter badge as its visual identity — the codebase has no existing
  per-child avatar/colour system, so this badge is the visual identity this
  feature introduces rather than a new persistent per-child attribute.
- The card is informational only: no completion action, no interaction.
- Children never see an unconfigured card. Adults instead see a small
  "isn't set up yet" note near the same spot, pointing at the admin tools.
- The adult "Whose Turn Is It?" tool (in the same collapsible tools list as
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

## Out of scope

Multiple simultaneous rotations, per-weekday rotations, skipping or swapping
an individual date, notifications/reminders, and any interaction with jobs,
approvals, or points — all as scoped by issue #112. Per-child avatars or
colours beyond the single-letter badge introduced here are not a general
identity system and are not implemented elsewhere.
