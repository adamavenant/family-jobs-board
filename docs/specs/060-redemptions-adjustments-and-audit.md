# Redemptions, manual adjustments, and audit

## Status

Partially specified. The **manual point adjustments** section below is
implemented by issue #83. Redemptions and the searchable audit view are not yet
specified or implemented; they need their own sections before their issues are
cut.

## Manual point adjustments

### Outcome and user value

An adult can add or remove points for a child, with a required reason, when the
job and behaviour flows don't cover a situation (a bonus, a penalty, a
correction). Each adjustment is its own ledger entry, so the balance and history
stay explainable. Children see adjustments in their existing points history but
cannot make them.

### Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Record a point adjustment | No | No | Yes |
| See adjustments in points history | No | Own | Not exposed as an adult points view |

### Domain rules and state

- A `PointAdjustment` records the child, the adjusting adult, a UTC instant, a
  signed non-zero whole-number amount, and a required reason (trimmed, at most
  500 characters).
- Each adjustment writes exactly one `PointsLedgerEntry` in the same
  transaction. It is the only ledger source that may be negative; job and
  behaviour awards stay non-negative.
- Adjustments are append-only. There is no edit or delete. A mistake is
  corrected by recording a new, opposite adjustment; both entries remain in
  history.
- The child must be an active child.
- **Negative balances.** An adjustment may take a balance below zero (unlike a
  future redemption), but only after explicit confirmation: if a removal would
  leave the balance below zero, the API rejects it with `409` unless the request
  sets `confirmNegativeBalance: true`. Adding points, or removing points that
  keep the balance at zero or above, needs no confirmation.
- The confirmation check and the write are not one atomic step. A concurrent
  change between them can move the balance, which only affects whether the
  warning was shown, not correctness of the ledger.

### Idempotency

The client sends a `requestId` (GUID) with each request and reuses it when
retrying after an error. A unique database index on the request ID guarantees
one adjustment and one ledger entry, including under concurrent retries.

- Same request ID and same child, adult, amount, and reason: the original result
  is returned with `200`, without needing the negative-balance confirmation
  again.
- Same request ID with different details: `409` with code `requestConflict`.

### Data and migration

`AddPointAdjustments` adds `point_adjustments` (unique `request_id`; checks that
`amount <> 0` and the reason is not blank) and `points_ledger_entries.
point_adjustment_id` with a unique index and foreign key. The ledger's
single-source check now covers three sources, and a new amount-sign check
requires `amount >= 0` for job and behaviour entries and `amount <> 0` for
adjustments. Existing rows are unaffected.

"Reset jobs and points" also deletes adjustments, since they are point sources.
The reset audit row does not yet count them.

### HTTP contract

- `POST /api/point-adjustments` — adult; body `{ requestId, childId, amount,
  reason, confirmNegativeBalance }`.
  - `201` (new) or `200` (replay) with the adjustment and the child's new
    balance.
  - `400` for a zero amount, a missing or overlong reason, or an unknown,
    inactive, or non-child member.
  - `409` with `code: "negativeBalanceConfirmationRequired"`, `currentBalance`,
    and `resultingBalance` when confirmation is needed.
  - `409` with `code: "requestConflict"` for a reused request ID.
  - `403` for children.
- `GET /api/today` point earnings carry `source: "manualAdjustment"`, `name`
  (the reason), a signed `points`, `awardedAtUtc`, and `loggedByDisplayName`
  (the adjusting adult).

### UI

- Adults get an "Adjust points" tool: child, add or remove, number of points,
  and reason. Removing points that would go negative shows a warning with the
  current and resulting balance and needs an explicit "Yes, adjust anyway". The
  confirmation resubmits exactly the warned values, so editing the form
  afterwards cannot change what is confirmed.
- Children see adjustments in their history with a signed amount, the reason,
  time, and the adjusting adult.

### Tests

Domain invariants and ledger sign rules; application orchestration, the
negative-balance policy, idempotency, race handling, and compensating entries;
PostgreSQL integration tests for the endpoint, authorization, validation,
confirmation handshake, retries and concurrency, history integration, database
constraints, and reset; Vitest tests for the adult and child views.

### Out of scope

Redemptions, a searchable audit view, adjusting several children at once, and
linking a correction to the adjustment it corrects.
