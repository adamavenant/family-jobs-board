# Redemptions, manual adjustments, and audit

## Status

Partially specified. The **manual point adjustments** section below is
implemented by issue #83, **redemptions** by issue #82, and the **points ledger
view** by issue #77. Issue #82 also removed negative balances and added
attribution to the ledger. The searchable audit view is not yet specified or
implemented; it needs its own section before its issue is cut.

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
  transaction. Adjustments and redemptions are the only ledger sources that
  remove points; job and behaviour awards stay non-negative.
- Adjustments are append-only. There is no edit or delete. A mistake is
  corrected by recording a new, opposite adjustment; both entries remain in
  history.
- The child must be an active child.
- **No negative balances.** Points are never taken below zero. A removal that
  would leave the balance below zero is rejected with `409` and the child's
  current balance, and nothing is written. Issue #82 removed the earlier
  "adjust anyway" confirmation. A balance that was already negative before
  then stays as recorded, because history is never rewritten.
- **Removals are serialized per child.** Every adjustment and redemption locks
  the child's `household_members` row (`SELECT … FOR UPDATE`) for its
  transaction. Under the lock it re-checks its request ID, reads the balance,
  applies the rule above, writes, and commits. Two removals for the same child
  can't both pass the check against the same balance. Job and behaviour awards
  only add points and take no lock.

### Idempotency

The client sends a `requestId` (GUID) with each request and reuses it when
retrying after an error. A unique database index on the request ID guarantees
one adjustment and one ledger entry, including under concurrent retries.

- Same request ID and same child, adult, amount, and reason: the original result
  is returned with `200`.
- Same request ID with different details: `409` with code `requestConflict`.
- The request ID is checked again after the child lock is taken. A retry that
  raced the original then replays it, rather than being checked against the
  balance the original already reduced.

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
  reason }`.
  - `201` (new) or `200` (replay) with the adjustment and the child's new
    balance.
  - `400` for a zero amount, a missing or overlong reason, or an unknown,
    inactive, or non-child member.
  - `409` with `code: "insufficientPoints"` and `currentBalance` when a removal
    would take the balance below zero.
  - `409` with `code: "requestConflict"` for a reused request ID.
  - `403` for children.
- Adjustments appear in `GET /api/points-ledger` (see the points ledger view
  below) with the reason as the name, a signed `points`, and the adjusting
  adult in `recordedByDisplayName`.

### UI

- Adults get an "Adjust points" tool: child, add or remove, number of points,
  and reason. Removing more points than the child has shows the server's
  message with the current balance, and nothing is recorded.
- Children see adjustments in their points ledger with a signed amount, the
  reason, the time, and who made them.

### Tests

Domain invariants and ledger sign rules; application orchestration, the
no-negative-balance rule, idempotency, race handling, and compensating entries;
PostgreSQL integration tests for the endpoint, authorization, validation, the
insufficient-points response, retries and concurrency, history integration,
database constraints, and reset; Vitest tests for the adult and child views.

### Out of scope

A searchable audit view, adjusting several children at once, and linking a
correction to the adjustment it corrects.

## Redemptions

### Outcome and user value

A child spends points on a reward. An adult records the redemption for the
child, saying what the reward was. Each redemption is its own ledger entry that
takes the points off the balance, so the child can see where their points went.
A redemption never takes a balance below zero, and a retried request never
redeems twice.

### Actors and authorization

| Capability | Anonymous | Child | Adult |
| --- | --- | --- | --- |
| Redeem a child's points | No | No | Yes |
| See redemptions in points history | No | Own | Any child, via the points ledger view |

### Domain rules and state

- A `PointRedemption` records the child, the redeeming adult, a UTC instant, a
  positive whole number of points, and a required reward (trimmed, at most 200
  characters). There is no rewards catalog; the reward is free text.
- Each redemption writes exactly one `PointsLedgerEntry` in the same
  transaction, with `amount = -points`. A redemption is its own ledger source,
  not a kind of adjustment.
- Redemptions are append-only. There is no edit, delete, or undo. A mistaken
  redemption is corrected with a manual adjustment that adds the points back.
- The child must be an active child.
- **No negative balances.** A redemption for more points than the child's
  current balance is rejected with `409` and the current balance, and nothing
  is written. Redeeming exactly the balance is allowed and leaves zero.
- **Serialized per child,** with the same lock and order of steps as
  adjustments (see above).

### Idempotency

The same as adjustments: the client sends a `requestId` (GUID), and a unique
database index on it guarantees one redemption and one ledger entry.

- Same request ID and same child, adult, points, and reward: the original
  result is returned with `200`.
- Same request ID with different details: `409` with code `requestConflict`.
- The request ID is checked again under the child lock.

### Data and migration

`AddPointRedemptions` adds `point_redemptions`:

- columns `id`, a unique `request_id`, `child_id`, `redeemed_by_member_id`,
  `points`, `reward`, and `redeemed_at_utc`;
- checks that `points > 0` and the reward is not blank;
- an index on `(child_id, redeemed_at_utc)`;
- restricting foreign keys to `household_members`.

It also adds `points_ledger_entries.point_redemption_id` with a unique index and
a foreign key. The single-source check now covers four sources. The amount-sign
check requires `amount >= 0` for job and behaviour entries, `amount <> 0` for
adjustments, and `amount < 0` for redemptions. Existing rows are unaffected.

"Reset jobs and points" also deletes redemptions. As with adjustments, the reset
audit row doesn't count them.

### HTTP contract

- `POST /api/point-redemptions` — adult; body `{ requestId, childId, points,
  reward }`.
  - `201` (new) or `200` (replay) with the redemption `{ id, childId,
    redeemedByMemberId, points, reward, redeemedAtUtc }` and the child's new
    balance.
  - `400` for a missing request ID, fewer than 1 point, a missing or overlong
    reward, or an unknown, inactive, or non-child member.
  - `409` with `code: "insufficientPoints"` and `currentBalance` when the child
    doesn't have enough points.
  - `409` with `code: "requestConflict"` for a reused request ID.
  - `403` for children.
- Redemptions appear in `GET /api/points-ledger` with the reward as the name, a
  negative `points`, and the redeeming adult in `recordedByDisplayName`.

### UI

- Adults get a "Redeem a reward" panel in the grown-up toolbox, after "Adjust
  points", with fields for the child, the number of points, and the reward.
  Opening the panel loads each active child's current balance. The chosen
  child's balance is shown and caps the points field.
- A successful redemption shows a confirmation with the new balance and resets
  the form. A retry after an error reuses the request ID. Asking for more
  points than the child has shows the server's message with the current
  balance.
- Children see redemptions in their points ledger with a negative amount, the
  reward, the time, and who redeemed them. Children cannot redeem.

### Tests

- Domain: points, reward, and the ledger sign rule.
- Application: validation, insufficient balance, redeeming exactly the
  balance, idempotent replay, request conflicts, and a retry that raced the
  original.
- PostgreSQL integration:
  - the endpoint, authorization, and validation;
  - insufficient points;
  - retries and concurrent duplicates;
  - concurrent redemptions that together exceed the balance, where only the
    affordable ones succeed;
  - a concurrent adjustment and redemption;
  - ledger integration with attribution;
  - database constraints and reset.
- Vitest: the redeem panel and the ledger.

### Out of scope

A rewards catalog or prices, children asking for rewards, undoing a redemption
(use an adjustment instead), and redeeming for several children at once.

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

- **Entry content.** Each entry has:
  - the name: the job name, the behaviour type name as it was logged, the
    adjustment reason, or the redeemed reward;
  - the award date and time;
  - the signed points;
  - the child's balance after the entry;
  - who recorded it: the adult who logged the behaviour, made the adjustment,
    or redeemed the points. This is shown to children and adults, including
    after that adult is deactivated;
  - the child's name, in the adult all-children view only.

  Descriptions and source labels are not shown. Job awards don't show who
  approved them yet, because approvals don't record the deciding adult. Issue
  #122 tracks that. Issue #82 reversed the earlier choice to leave out the
  attributing adult: seeing who gave or spent points is useful transparency in
  a household, and matches the product brief's "approving parent".
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
              balanceAfter, awardedAtUtc, recordedByDisplayName }],
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
- `recordedByDisplayName` is the recording adult's display name, or `null`
  for job awards (see #122).

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
- Each entry shows "by <adult>" when it has a recording adult.
- The page has empty states (household-wide, for one child, and for the child
  viewer), a load error with a way back to the board, and sign-out from the
  page.

### Tests

- Application tests: access rules, filter and cursor validation, the listed
  children, paging, and balance-after across pages.
- PostgreSQL integration tests:
  - the endpoint, and authorization for each role;
  - names and recording adults from each ledger source;
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
- The audit trail (#84).
