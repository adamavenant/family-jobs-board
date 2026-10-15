# Undo toast and household settings

## Status

Implemented by issue #139. Like `080-whose-turn-rotation.md`, this is a
cross-cutting feature outside the `PLAN.md` phase arc, so its spec is written
alongside the implementation. The design decision is recorded in
`docs/adr/0004-undo-by-delaying-saves.md`.

## Outcome and user value

Any time a grown-up or child does something on the board, a small toast
confirms it and offers **Undo** for the household's undo time (30 seconds
unless a grown-up changes it). Pressing Undo means the action never happened:
nothing reaches the server. Grown-ups set the undo time once, in the app, and
every device in the household uses it.

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Undo their own queued actions on this device | No | Yes | Yes |
| Read the household's undo time | No | Yes | Yes |
| Change the household's undo time | No | No (`403`) | Yes |

## Scope

Actions that wait behind the undo toast:

- completing a job (a child's "Mark as done" and a grown-up's "Mark as done
  for…"), approving, and rejecting;
- adding, editing, and cancelling a once-off job; creating a recurring job and
  applying a scoped recurring change;
- logging a good behaviour, adjusting points, and redeeming a reward;
- creating, editing, and deleting a good-behaviour type;
- creating, updating, deactivating, and restoring a family member;
- starting, changing, and ending a Whose Turn rotation.

Not covered, and saved at once: sign in and out, bootstrap, PIN setup and
reset, "Reset jobs and points" (it already needs a typed confirmation),
previewing a recurring change, and saving the undo time itself.

## Domain rules

- The undo time is a household setting of whole seconds from 5 to 120. A
  household that has never saved it uses 30 seconds. There is one setting per
  household; the last grown-up to save it wins.
- Undo is a property of the browser, not the server: an action is queued on the
  device, shown as already done, and sent only when its toast expires or is
  dismissed. Undo drops it without a request. No reversal endpoints exist.
- Each queued action is sent at most once. Several queued actions are
  independent: each has its own toast, countdown, and request.
- Queued actions are sent straight away when the page is hidden
  (`visibilitychange`) or closed (`pagehide`), and before signing out, signing
  in, or finishing PIN setup, so they are sent with the identity that made
  them. Saves use `fetch` `keepalive` so they can finish as the page closes.
- Validation that the browser can check (required fields, ranges, choices)
  still happens before queueing, so mistakes are shown at once. Rules only the
  server can check (stale points, insufficient points, conflicts) are reported
  when the queued action is sent.

## Data and migration

Forward-only migration `AddHouseholdSettings` adds `household_settings`:
`id` (always 1), `undo_grace_period_seconds`, and nullable `updated_at_utc`
and `updated_by_member_id` (restricted foreign key to `household_members`).
Check constraints keep a single row and the range 5–120. No row is seeded; the
API answers with the default until the first save creates it. "Reset jobs and
points" leaves the setting alone.

## HTTP contract

- `GET /api/household-settings` (any signed-in member) returns
  `{ undoGracePeriodSeconds, minimumUndoGracePeriodSeconds,
  maximumUndoGracePeriodSeconds }`.
- `PUT /api/household-settings` (adults only) takes `{ undoGracePeriodSeconds }`.
  Missing, fractional, or out-of-range values return `400` validation problem
  details keyed `UndoGracePeriodSeconds`; children receive `403`.
- `GET /api/today` also returns `undoGracePeriodSeconds`, so each device picks
  up a changed undo time whenever its board loads or revalidates.

## UI states

- **Queued:** the toast shows a short description (for example "Approved
  “Feed the dog” for Sam: +5 points"), a visible countdown, **Undo**, and a
  dismiss control that saves straight away. The item shows its after-action
  state: jobs appear approved, sent for approval, rejected, edited, or gone;
  counts update; new jobs, members, behaviour types, and rotations appear as
  "Being added"; balances in the redeem form count queued spends. Controls on a
  queued item are disabled so it can't be acted on twice.
- **Undone:** the toast closes and the item returns to its previous state.
- **Saved:** the toast closes, the request is sent once, and the page
  revalidates to the server's state.
- **Refused:** an error toast (`role="alert"`) names the action and the
  server's reason, offers **Try again** (which resends the same request,
  including its request ID), and the page revalidates to the server's state.
  Form entries cleared at queue time are not restored.
- **Settings:** grown-ups find "Undo time" in the grown-up tools; it loads the
  current value on open, saves at once, and confirms with a message. Children
  don't see it.
- **Accessibility:** each toast's description has `role="status"`; Undo is a
  native button reachable by keyboard and described by the toast's text; the
  countdown is hidden from screen readers so it isn't announced every second,
  and it pauses (showing "Paused") while the toast has keyboard focus or the
  pointer is over it. Toasts stack inside the screen at phone widths, scroll
  when there are many, and the page reserves space below so content behind
  them can be scrolled into view. The progress bar respects reduced motion.

## Audit and security

The setting records the adult who last changed it and when. Authorization is
enforced by the API's `Adult` policy, not by hiding the setting. Queued actions
are held only in the page's memory and are never persisted in browser storage.

## Acceptance scenarios

- Given a grown-up approves a job, then an Undo toast shows for the configured
  duration, the job appears approved, and no API request is sent until the
  toast expires or is dismissed.
- Given Undo is pressed, then no request is sent and the item returns to its
  previous state.
- Given the toast expires or is dismissed, then the action is sent exactly once
  and the page revalidates.
- Given the API rejects a queued action, then an error toast explains what
  failed and the UI reflects the server's state.
- Given queued actions when the tab is hidden or closed, or the user signs out,
  then they are sent first.
- Given a grown-up changes the undo time, then later toasts on every device use
  the new duration; a new household defaults to 30 seconds.
- Values outside 5–120 seconds are rejected with a validation error, and
  children can't change the setting (`403`).
- Keyboard and screen-reader users can reach Undo, and the countdown pauses
  while the toast is focused or hovered.

## Automated tests

Domain and application tests cover the setting's range, default, audit, and a
concurrent first save. PostgreSQL integration tests cover the endpoints,
authorization, validation, the board field, simultaneous first saves, the
database constraints, and reset. Vitest (fake timers) covers the queue,
the toast, every undoable panel, flushing on hide/close and before sign-out,
`keepalive`, and the settings panel. Playwright covers queueing, Undo, expiry,
dismissal, refused saves, sending on hide/close and before sign-out, keyboard
pausing, and stacked toasts at phone width.

## Compose demonstration

Run `docker compose up --build`, sign in as a grown-up at
<http://localhost:3000>, approve a job, and watch the toast count down; press
Undo or let it expire. Open "Undo time" in the grown-up tools to change the
duration, then act on another device.

## Unresolved decisions

- Timers in a background tab may be throttled by the browser; this is harmless
  because hiding the tab sends queued actions at once.
- A queued action on a device that loses power before the page can send it is
  lost; the household accepted this trade-off for undo without server
  endpoints.
