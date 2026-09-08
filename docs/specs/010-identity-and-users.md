# Identity and users

## Status

Accepted for the first-adult bootstrap, PIN sign-in, and family-member
list/create/onboarding implementation slices. Editing, soft deletion,
restoration, and PIN reset remain specified at a boundary level only.

## Outcome and user value

A fresh household can create its first adult, and an upgraded pilot household
can claim an existing adult without losing its history. Family members then
choose their profile, enter a role-appropriate PIN, and receive a revocable
same-origin session. The API—not the visibility of controls—enforces what an
adult or child may do.

This replaces the development profile switcher as the production identity
boundary while retaining every existing member, job, recurrence, review, and
points-ledger record.

## Actors and authorization

| Capability                                     | Anonymous                | Child                 | Adult                                  |
| ---------------------------------------------- | ------------------------ | --------------------- | -------------------------------------- |
| Read bootstrap/sign-in state                   | Yes                      | Yes                   | Yes                                    |
| Bootstrap first adult or claim pilot household | Before bootstrap only    | No                    | No                                     |
| View sign-in-ready member choices              | Yes                      | Yes                   | Yes                                    |
| Sign in as selected profile with its PIN       | Yes                      | Yes                   | Yes                                    |
| Refresh or end own session                     | By refresh cookie        | Yes                   | Yes                                    |
| View Today board                               | No                       | Own data              | All children's jobs and approval count |
| Submit job completion                          | No                       | Own eligible job only | No                                     |
| Add jobs or recurring schedules                | No                       | No                    | Yes                                    |
| Approve or reject work                         | No                       | No                    | Yes                                    |
| Start one-time PIN handoff                     | No                       | No                    | Yes                                    |
| Finish one-time PIN handoff                    | Valid handoff token only | Target                | Target                                 |

Health endpoints remain anonymous and reveal no household or identity data.
All other current `/api` operations require a valid session. The authenticated
member ID and role come from the server-validated session; clients no longer
supply `viewerId` or choose another member through a query parameter.

`GET /api/today` is the first protected boundary proof: anonymous requests get
`401`; a child receives only their own board; an adult receives the household
adult view. The same policies then cover every existing write endpoint in this
slice.

## Scope

### First implementation slice

- bootstrap state for a fresh or upgraded household;
- first-adult creation or existing-adult claim;
- PIN setup with confirmation in the UI;
- sign-in-ready user chooser and PIN sign-in;
- short-lived access JWTs plus revocable, rotating server-side sessions;
- ten-minute inactivity expiry, activity-based renewal, and logout;
- failed-PIN throttling;
- adult and child policies on every existing application endpoint;
- one-time adult-authorized PIN handoff for an existing unconfigured member;
- retirement of the production demo profile switcher; and
- a non-destructive migration from the deployed pilot data.

### Family-member onboarding slice

- an authenticated adult can list every active profile, including members whose
  credential is `NotSet`;
- an authenticated adult can create an `Adult` or `Child` with required first
  name and surname, optional nickname, and a `NotSet` credential;
- creation never accepts a PIN and commits the member and credential together;
- the adult can deliberately start the existing one-time PIN handoff immediately
  or return to it later; and
- anonymous and child callers cannot list or create household members.

### Out of scope

- editing, deleting, restoring, or resetting users beyond the existing-profile
  PIN handoff;
- multiple households or tenants;
- email, passwords, recovery links, external identity providers, biometrics,
  multi-factor authentication, or device trust;
- public-internet exposure or TLS delivery changes;
- long-lived remembered-device sessions;
- administrator impersonation; and
- changing job, recurrence, approval, or points rules except to enforce the
  authenticated actor.

## Domain rules and state

### Household bootstrap

The database contains one Identity-owned household-bootstrap row. Its states
are `Required` and `Complete`; completion records the first authenticated adult
and a UTC instant. Bootstrap runs in one PostgreSQL transaction which locks
that row. Two concurrent requests are deterministic: one succeeds and the
other receives `409 household_already_bootstrapped`.

Bootstrap mode is derived as follows:

1. No household members: `CreateFirstAdult`. The request creates an adult with
   first name, surname, and PIN credential. A child role is not accepted.
2. Members exist and no credential is ready: `ClaimExistingAdult`. The response
   lists only active adult profiles eligible to be claimed. The request selects
   one and supplies any missing surname plus its new PIN.
3. Bootstrap is complete: `SignIn`. Bootstrap writes are permanently closed.

Bootstrap completion and the first credential commit atomically.

### Roles and names

`Adult` and `Child` are explicit persisted role values. Existing `is_adult`
values migrate losslessly. First name and surname are required before a profile
becomes sign-in-ready. Nickname remains optional.

The chooser normally shows nickname, otherwise first name. When two active,
sign-in-ready profiles would have the same displayed name, append surname to
both. Matching is case-insensitive after trimming. IDs—not names—identify
profiles.

### Credential state and PINs

A profile credential is `NotSet` or `Ready`. `NotSet` profiles never appear in
the normal chooser and cannot sign in. They may appear only to the bootstrap
claim flow or an authenticated adult's PIN-handoff screen.

- adult PIN: exactly six ASCII digits;
- child PIN: exactly four ASCII digits;
- the PIN is a string, so leading zeroes are preserved;
- whitespace, signs, separators, non-ASCII numerals, and other characters are
  rejected;
- confirmation must match before the UI submits; the API accepts one PIN and
  independently enforces role and format; and
- PINs are never encrypted, stored as numbers, returned after setup, included
  in URLs, or written to logs, traces, metrics, audit, exceptions, or errors.

Use `PasswordHasher<HouseholdMember>`, not a bespoke fast digest. Before hash
or verification, derive a fixed-length secret from the PIN with HMAC-SHA-256
and a deployment-only pepper, then pass it to the versioned salted hasher.
Configure Identity V3 PBKDF2 to at least 220,000 HMAC-SHA-512 iterations;
benchmark on Serendipity and keep verification approximately within 100–250 ms.
On `SuccessRehashNeeded`, replace the hash in the same transaction.

The pepper is at least 32 random bytes and lives outside Git as
`Authentication__PinPepper`. Production startup fails when it or the JWT key
is absent. Pepper loss or rotation requires PIN reset. For an unknown,
inactive, or `NotSet` profile, perform one dummy hash verification before the
same generic failure.

### PIN handoff

An authenticated adult may initiate setup for one active `NotSet` profile. The
server creates a random, single-use token, stores only its hash, and expires it
after five minutes. A newer token for the target revokes the previous one.

Issuing the handoff token, revoking the authorizing adult session, and clearing
its refresh cookie are one server operation. Return the token once and hold it
only in browser memory—not a URL, storage API, log, or cookie. The UI clears the
adult access token and shows the target PIN/confirmation screen. Refreshing or
closing loses the token and requires the adult to sign in and start again.
Successful setup atomically consumes the token and credential transition, then
signs in the target. Concurrent or repeated consumption has exactly one winner;
failure never restores the adult session.

### Sessions and inactivity

The session design is accepted in
[ADR 0003](../adr/0003-pin-authentication-and-sessions.md).

- Access token: signed JWT, five-minute lifetime, response body only, held in
  JavaScript memory.
- Refresh token: 256 random bits in a host-only `HttpOnly`, `SameSite=Strict`
  cookie scoped to `/api/auth`; only its hash is persisted.
- Server session: member, role snapshot, refresh hash, created time, last
  activity, revocation time, and rotation version.
- Inactivity: invalid when last server-observed activity is ten minutes old;
  equality with the expiry instant is expired.
- Activity: a successful protected API request or refresh—not mouse movement
  alone. Persist at most once per minute.
- Renewal: refresh only after recent user interaction when the JWT is near
  expiry or after reload; never renew forever in a hidden idle tab.
- Rotation: every refresh invalidates the old token. Old-token reuse revokes
  the session.
- Logout: revoke session, clear cookie, and clear in-memory JWT.
- Revocation/expiry: check server-side on every protected request so a validly
  signed JWT cannot outlive its session.

Validate JWT signature, fixed issuer and audience, expiry, subject, role,
session ID, issued-at, and token ID. Production uses an external base64 key of
at least 32 random bytes at `Authentication__JwtSigningKey`. No CORS origins
are enabled.

Refresh and logout are same-origin `POST` requests whose Origin must match a
configured application origin. Cookies use `Secure` for HTTPS. Plain HTTP on
the trusted LAN is an acknowledged MVP risk; public exposure is forbidden and
LAN HTTPS remains Phase 7 work.

### Failed attempts

Two controls apply to `POST /api/auth/sign-in`:

1. ASP.NET Core rate limiting permits 20 attempts per remote IP in a rolling
   five-minute window, with no queue.
2. A persisted profile counter locks that profile for 60 seconds after five
   failed complete-PIN attempts inside five minutes. Success resets the count;
   the window resets after five minutes.

Counter changes are concurrency-safe. Invalid PIN syntax consumes the IP
limit. Wrong credentials return `401 invalid_credentials`; a limited IP or
locked profile returns the same `429 try_again_later` with `Retry-After`.
Responses never say whether a profile, role, prefix, or digit was correct.
Use the connection address unless forwarded headers came from the explicitly
trusted same-host reverse proxy; never trust an arbitrary client-supplied
forwarded address when partitioning the limiter.

## Persistence and migration

Identity owns these PostgreSQL records:

- `household_bootstrap`: singleton state, first-adult ID, completion instant;
- `member_credentials`: member ID, versioned PIN hash, readiness/set times,
  failed-attempt window/count, and lock expiry;
- `auth_sessions`: session ID, member ID, role snapshot, refresh hash/rotation,
  created/last-activity/revoked times; and
- `pin_setup_tokens`: target member, token hash, expiry, consumed/revoked times,
  and authorizing adult.

Persist all instants in UTC. Hash/token columns have maximum lengths. Foreign
keys restrict deletion while history is required. Unique constraints permit
one credential per member and one bootstrap result. Correctness does not depend
on immediate expired-session or token cleanup.

The forward migration:

1. creates the Identity tables and singleton bootstrap row;
2. converts `is_adult` to explicit role without changing member IDs;
3. adds nullable surname so existing rows remain valid;
4. leaves every member, job, series, occurrence, review, and ledger reference
   unchanged;
5. gives existing profiles `NotSet` credentials and leaves bootstrap
   `Required`, selecting `ClaimExistingAdult` mode;
6. stops unconditional demo seeding in production; and
7. keeps demo seeding only behind an explicit development/test setting which
   production Compose does not set.

On the live pilot, Addie or Hellie can be claimed. The other adult and children
retain their data and receive PINs by handoff. No default PIN is assigned. A
schema downgrade fails clearly once identity data exists; application rollback
must remain compatible with the forward schema.

## HTTP contract

Errors use RFC Problem Details with a stable `code` extension. PINs, tokens,
hashes, lock counters, and internal bootstrap details never appear in errors.

### `GET /api/auth/start`

Anonymous. Returns exactly one state:

```json
{ "state": "createFirstAdult" }
```

```json
{
  "state": "claimExistingAdult",
  "adults": [{ "id": "uuid", "displayName": "Addie" }]
}
```

```json
{
  "state": "signIn",
  "members": [{ "id": "uuid", "displayName": "Fredster", "role": "child" }]
}
```

Inactive and `NotSet` profiles are omitted from normal `members`.

### `POST /api/auth/bootstrap`

Anonymous only while required. A fresh request is:

```json
{
  "mode": "createFirstAdult",
  "firstName": "Addie",
  "surname": "Avenant",
  "pin": "012345"
}
```

Upgrade uses `mode: "claimExistingAdult"`, `memberId`, surname, and PIN.
Success returns `201 AuthResponse` and sets the refresh cookie. Errors are
`400 invalid_bootstrap`, `409 household_already_bootstrapped`, and
`409 adult_not_claimable`.

### `POST /api/auth/sign-in`

Accepts `{ "memberId": "uuid", "pin": "0123" }`. Returns
`200 AuthResponse`, `401 invalid_credentials`, or `429 try_again_later` with
`Retry-After`, and sets the refresh cookie only on success.

### `POST /api/auth/refresh`

No body. Rotates the refresh cookie. Returns `200 AuthResponse` or
`401 session_expired`; old-token replay revokes the session.

### `POST /api/auth/logout`

Accepts current access token or refresh cookie, always clears the cookie, and
returns `204`. Repeated logout is harmless.

### `POST /api/users/{memberId}/pin-setup`

Adult policy. Accepts an optional `surname`; it is required when the migrated
target does not have one and is ignored when their surname is already present.
Returns one-time `setupToken`, `expiresAtUtc`, and target display name. Errors
are `404 member_not_found`, `409 pin_already_set`, and `409 member_not_eligible`.

### `POST /api/auth/setup-pin`

Anonymous with `{ "setupToken": "opaque", "pin": "0123" }`. Returns
`200 AuthResponse` and consumes the token, or generic
`400 invalid_or_expired_setup`.

### `AuthResponse`

```json
{
  "accessToken": "jwt",
  "accessTokenExpiresAtUtc": "2026-09-06T08:00:00Z",
  "member": {
    "id": "uuid",
    "displayName": "Fredster",
    "role": "child"
  }
}
```

Generated TypeScript owns these contracts. Presentation components do not
handcraft API payload types.

## UI states and accessibility

The root route loader calls `/api/auth/start`, then renders exactly one of:

- fresh-household bootstrap;
- pilot existing-adult claim;
- member chooser;
- PIN entry for the selected member;
- one-time PIN handoff; or
- authenticated Today board.

Forms cover loading, validation, submitting, success, server error, expiry, and
retry. Incorrect PIN returns focus to the chooser with a generic message.
Expired sessions clear in-memory identity and return there too.

Use one native labelled input with numeric input mode, appropriate one-time PIN
autocomplete, maximum length, and password-like display. Do not split digits
into focus-jumping fields. Provide visible focus, status/error announcements,
keyboard operation, 44px touch targets, and iPhone/Kindle Fire layouts. Never
put PIN or setup token in route state, URLs, analytics, or persistent storage.

## Audit, observability, and health

Audit bootstrap, credential setup/reset, successful sign-in, logout, session
revocation, setup-token issue/consume, and authorization denial with UTC time,
known actor/target IDs, and correlation ID. Failed sign-in may include member
ID, remote-IP hash, outcome category, and lock duration—never submitted input
or token material.

Emit counters for sign-in outcome, rate-limit rejection, active/revoked
sessions, and bootstrap conflicts. Logs use stable event names and never log
request bodies or authentication headers. Health checks do not verify
credentials or expose counts; database readiness covers migrated tables.

## Acceptance examples

1. **Fresh bootstrap:** Given no members, when an adult submits valid names and
   matching six-digit confirmation, then one adult, credential, bootstrap
   completion, and session commit atomically and Today opens.
2. **First-user rule:** A child bootstrap is rejected with no committed row.
3. **Concurrency:** Two valid bootstrap requests produce one success and one
   `household_already_bootstrapped`.
4. **Pilot upgrade:** Claiming Addie preserves all existing IDs and historical
   references; only Addie becomes sign-in-ready.
5. **Incomplete profile:** Fredster remains absent from the normal chooser until
   an adult completes handoff.
6. **PIN format:** Child PIN `0123` retains its zero and works; `123`, spaces,
   and non-ASCII digits fail generically.
7. **Throttling:** Five failures in five minutes lock that profile; another
   attempt returns generic `429` and cannot authenticate.
8. **Role:** A child calling add, recurrence, approve, reject, or handoff gets
   `403` and changes no data.
9. **Ownership:** A child completing another child's job gets `403`.
10. **Inactivity:** At ten minutes without server-observed activity, refresh
    and protected calls fail and the UI returns to chooser.
11. **Renewal:** Refresh inside the window rotates the token, advances activity,
    and makes the old token unusable.
12. **Logout/replay:** Logout or refresh replay makes an otherwise valid JWT
    fail the live session check.
13. **Restart:** With the same secrets/database, configured users can sign in
    and an eligible session can refresh after container restart.

## Automated test seams

- **Domain:** PIN formats, credential transitions, bootstrap state, one-time
  token expiry, and exact inactivity boundaries.
- **Application:** atomic/concurrent bootstrap, dummy verification, throttling,
  refresh rotation/replay revocation, handoff logout, and authorization.
- **PostgreSQL/API:** deployed-data migration and foreign-key preservation,
  unique constraints, races, real hashing, all protected endpoints,
  401/403/409/429 errors, restart, and missing production secrets.
- **React:** auth-state routing, confirmation/leading zero, generic errors,
  focus/status behavior, in-memory tokens, expiry, and role views.
- **Playwright:** fresh bootstrap; pilot claim and child handoff; child
  completion then adult approval; expiry; phone and tablet widths.
- **Security:** inspect logs, errors, OpenAPI, DOM, URL, storage, cookies, and DB
  to prove no PIN, plaintext refresh token, signing key, or pepper appears.

Use a controllable clock. Integration tests use real PostgreSQL and public HTTP,
not EF InMemory.

## Compose demonstration

1. Configure test secrets outside Git and start normal Compose.
2. Reset only a disposable volume; verify bootstrap at `http://localhost:3000`.
3. Bootstrap an adult, restart without removing volumes, and sign in again.
4. Restore a pilot DB copy, migrate, claim an adult, and prove all job/review/
   points history remains.
5. Handoff to a child, complete work, then sign in as adult and approve it.
6. Render production Compose and prove auth secrets are required but absent
   from Git, image history, and logs.

Production remains `http://dashboard.home.arpa`; API/database stay private and
no router port forwarding is added.

## Resolved decisions

- Claim existing profiles in place; never delete, replace, or assign a default
  PIN.
- Keep access JWTs in memory and rotating refresh tokens in a host-only HttpOnly
  cookie backed by a revocable PostgreSQL session.
- Ten minutes means server-observed inactivity, not absolute login age or mouse
  movement alone.
- Use framework password hashing plus a separate deployment pepper, benchmarked
  on Serendipity.
- Handoff logs out the adult and signs in the configured target.
- Authenticate every existing non-health endpoint in the first slice; adult
  and ownership policies are not deferred.

There are no unresolved decisions blocking the implementation issue.

## References

- [Microsoft: Hash passwords in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/password-hashing?view=aspnetcore-10.0)
- [Microsoft: Minimal API authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0)
- [Microsoft: Configure JWT bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [Microsoft: ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [OWASP: Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
