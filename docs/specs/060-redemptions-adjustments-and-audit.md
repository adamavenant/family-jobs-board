# Redemptions, manual adjustments, and audit

## Status

Partially specified. The **manual point adjustments** section below is
implemented by issue #83 and the **points ledger view** by issue #77.
Redemptions and the searchable audit view are not yet specified or
implemented; they need their own sections before their issues are cut.

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
| See adjustments in points history | No | Own | Any child, via the points ledger view |

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
- Adjustments appear in `GET /api/points-ledger` (see the points ledger view
  below) with the reason as the name and a signed `points`.

### UI

- Adults get an "Adjust points" tool: child, add or remove, number of points,
  and reason. Removing points that would go negative shows a warning with the
  current and resulting balance and needs an explicit "Yes, adjust anyway". The
  confirmation resubmits exactly the warned values, so editing the form
  afterwards cannot change what is confirmed.
- Children see adjustments in their points ledger with a signed amount, the
  reason, and the time.

### Tests

Domain invariants and ledger sign rules; application orchestration, the
negative-balance policy, idempotency, race handling, and compensating entries;
PostgreSQL integration tests for the endpoint, authorization, validation,
confirmation handshake, retries and concurrency, history integration, database
constraints, and reset; Vitest tests for the adult and child views.

### Out of scope

Redemptions, a searchable audit view, adjusting several children at once, and
linking a correction to the adjustment it corrects.

## Points ledger view

### Outcome and user value

Children and adults can read the points ledger, newest first, with each
child's running balance. A child sees only their own ledger, opened by tapping
their points total on the board. An adult sees every child's ledger and can
filter to one child, so they can compare how the children are earning. The
children already compare their points with each other, which is what prompted
this view.

### Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Read a child's ledger and balance | No | Own only | Any child |
| Read every child's ledger at once | No | No | Yes |
| Change ledger entries from the view | No | No | No |

### Rules

- **Entries are deliberately lean.** Each entry has:
  - the name: the job name, the behaviour type name as it was logged, or the
    adjustment reason;
  - the award date and time;
  - the signed points;
  - the child's balance after the entry;
  - the child's name, in the adult all-children view only.

  Descriptions, source labels, and the attributing adult are not shown. The
  product brief's "approving parent" is dropped on purpose, and job approvals
  don't record a reviewer anyway.
- **Order.** Newest first by `awarded_at_utc`, then entry ID.
- **Paging.** Pages hold 20 entries. The next page starts strictly after the
  last entry's `(awarded_at_utc, id)` position, using a PostgreSQL row
  comparison, so entries that share a timestamp are neither skipped nor
  repeated.
- **Balances** are sums of ledger entries and are never stored.
  - A page's balance-after values come from each child's sum at or before the
    page's first entry, walked down the page.
  - Every entry for that child between the top of the page and their first
    row is on the page, so balances carry on correctly across pages.
- **Children listed for adults**, in order:
  1. Active children.
  2. Deactivated children who have ledger entries, marked inactive.

  Each child is shown with their current balance. An adult may filter to any
  child in the household, including a deactivated one.
- **Children** always get their own ledger. Asking for any other child is
  refused rather than silently answered.

### HTTP contract

`GET /api/points-ledger?childId={uuid?}&before={cursor?}`, for any signed-in
member, returns `200` with:

```
{
  selectedChildId: uuid | null,
  children: [{ id, displayName, isActive, balance }],
  entries: [{ id, childId, childDisplayName, name, points,
              balanceAfter, awardedAtUtc }],
  nextCursor: string | null
}
```

- Pass `nextCursor` as `before` to read the next, older page. The cursor is
  opaque.
- **Child viewer:** `selectedChildId` is always their own ID, and `children`
  holds only them. Any other `childId` returns `403`.
- **Adult viewer:** an unknown ID, an adult's ID, or a malformed `childId`
  returns a `400` validation problem.
- **Anyone:** a malformed cursor returns `400`.

`GET /api/today` keeps `pointsBalance` for children and no longer returns
`pointEarnings`.

### Data and migration

No new tables. `AddPointsLedgerTimelineIndex` replaces the single-column
`child_id` index on `points_ledger_entries` with
`ix_points_ledger_entries_child_timeline` on
`(child_id, awarded_at_utc, id)`. That index serves the foreign key, per-child
paging, and balance sums.

### UI

- The `/points` page serves both roles and is linked from:
  - the child's points total on the board;
  - a **Points** link in the adult header, next to **Calendar**.

  The board no longer shows the history inline.
- Adults get a "Show points for" filter: All children, then each child with
  their current balance. The filter is kept in the URL as `childId`, so a
  reload keeps it, and focus stays on the chosen link.
- "Show older entries" appends the next page. A failed load shows an error and
  keeps what is already listed.
- The page has empty states (household-wide, for one child, and for the child
  viewer), a load error with a way back to the board, and sign-out from the
  page.

### Tests

- Application tests: access rules, filter and cursor validation, the listed
  children, paging, and balance-after across pages.
- PostgreSQL integration tests:
  - the endpoint, and authorization for each role;
  - names from each ledger source;
  - paging order against PostgreSQL's own ordering when timestamps tie;
  - deactivated children;
  - validation;
  - `/api/today` no longer returning history.
- Vitest tests: the child's tap-to-open, the adult filter and keyboard focus,
  paging and its failure, empty and error states, and sign-out.

### Out of scope

- Filtering by date or source, search, and export.
- Totals for a period, such as points earned this week.
- Editing entries from the view.
- Redemptions (#82). They add a ledger source, and their entries must appear
  here.
- The audit trail (#84).
