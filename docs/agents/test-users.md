# Test Users

Two test users exist for browser testing. They live in the database of the live household at `dashboard.home.arpa`; nothing in this repository creates them (there is no seed or migration for them).

| Role     | Name        |
| -------- | ----------- |
| Grown-up | Mom Tess    |
| Child    | Kid Tester  |

## PINs

The PINs are deliberately not stored in this repository, which is public. Ask the household owner for them. Never commit them, or paste them into issues, pull requests, logs, or test fixtures.

## Testing on the live household

- These accounts sit alongside the real family members. Both appear in the sign-in picker, and anything created with them shows on the real board.
- Label test jobs clearly (for example, start the name with `TEST`) and clean up afterwards where the app allows it. Recurring jobs generate many occurrences, so prefer once-off jobs unless recurrence is what is being tested.
- Do not deactivate these users or change their PINs unless the household owner asks.
