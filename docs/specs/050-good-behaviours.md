# Good behaviours

## Status

Implemented by issue #81. This spec was written alongside the implementation
because no Phase 5 spec existed when the issue was cut.

## Outcome and user value

Adults define the good behaviours the family cares about (for example Showing
Kindness, Being Helpful, Being Brave), each with a usual point value. When an
adult sees one, they log it against a child and the points are awarded
immediately, with the amount editable at that moment. Children can see which
behaviours exist and what they usually earn, and their points history shows
behaviour awards alongside job awards.

## Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| List active behaviour types (name, description, usual points) | No | Yes | Yes |
| Create, edit, or delete a behaviour type | No | No | Yes |
| Log a behaviour for a child | No | No | Yes |
| See behaviour awards in points history | No | Own | Not exposed as an adult points view |

Children are shown the usual points so expectations are transparent. The amount
actually awarded can differ, and history always shows the amount awarded.

## Domain rules and state

- A `GoodBehaviourType` has a name (1–100 characters), a description (up to 500
  characters, optional), and non-negative usual points.
- Deleting a type is a soft delete: it records who and when, disappears from the
  list, and can no longer be logged or edited. There is no restore in this
  slice. Repeating a delete succeeds.
- A `GoodBehaviour` log entry records the type, the child, the logging adult,
  the logging instant (UTC), the points awarded, and a copy of the type's name
  and description at that moment. Editing or deleting a type never changes log
  entries or the ledger.
- Points default to the type's usual points and may be overridden per log with a
  non-negative whole number.
- The child must be an active child. The type must exist and be active.
- Logging writes the behaviour and one `PointsLedgerEntry` in a single
  transaction. Balances remain derived from the ledger.

## Idempotency

The client sends a `requestId` (GUID) with each log request and reuses it when
retrying. A unique database index on the request ID guarantees one log and one
ledger entry, including under concurrent retries.

- Same request ID and same details: the original result is returned with `200`
  and no second award, even if the type was edited or deleted in between.
- Same request ID with a different type, child, adult, or explicit points value:
  `409`.

Omitting `points` on a retry means "the default", so it matches the original.

## Data and migration

`AddGoodBehaviours` adds `good_behaviour_types` and `good_behaviours`, makes
`points_ledger_entries.job_id` nullable, and adds
`points_ledger_entries.good_behaviour_id`. A check constraint requires exactly
one of the two sources; a unique index on each source column allows at most one
ledger entry per job and per behaviour. Existing job entries are unaffected.

Demo seeding adds Showing Kindness, Being Helpful, and Being Brave. Real
households start with no behaviour types.

The "reset jobs and points" administration action also deletes behaviour logs
(they are point sources) and keeps behaviour types. The reset audit row does not
yet count deleted behaviour logs.

## HTTP contract

- `GET /api/good-behaviour-types` — any signed-in member; active types.
- `POST /api/good-behaviour-types` — adult; `201` with the type; `400` for
  invalid data.
- `PUT /api/good-behaviour-types/{id}` — adult; `200`, `400`, `404`, or `409`
  when the type was deleted.
- `DELETE /api/good-behaviour-types/{id}` — adult; `204`, or `404` for an
  unknown type.
- `POST /api/good-behaviours` — adult; body `{ requestId, typeId, childId,
  points? }`; `201` (new) or `200` (replay) with the behaviour and the child's
  new balance; `400` for an unknown or deleted type, an inactive or non-child
  member, or negative points; `409` for a request-ID conflict.
- Children receive `403` on every write endpoint.
- `GET /api/today` point earnings carry `source` (`job` or `goodBehaviour`),
  `name`, optional `jobId`, `points`, `awardedAtUtc`, and `loggedByDisplayName`
  (behaviour entries only). Job entries do not currently record an approving
  adult.

## UI

- Adults get a "Good behaviours" tool on the board: a log form (child, type,
  editable points defaulting to the type's), and type management with
  confirmation before delete.
- Children get a read-only "Ways to earn points" list and see behaviour entries
  in their history with the logging adult.

## Tests

Domain invariants and snapshotting; application orchestration, idempotency, and
race handling; PostgreSQL integration tests for the endpoints, authorization,
retries and concurrency, history integration, immutability after edit or delete,
the ledger source constraint, and reset; Vitest tests for the adult and child
views.

## Out of scope

Restoring deleted types, negative or manual adjustments, redemptions, and
per-child type visibility (Phase 6 and later).
