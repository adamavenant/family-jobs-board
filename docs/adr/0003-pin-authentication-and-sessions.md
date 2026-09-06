# 0003 - Use peppered PIN hashes and revocable same-origin sessions

## Status

Accepted

## Context

Family Jobs Board uses short numeric PINs for children and adults on a trusted
home LAN. PINs have much less entropy than passwords, so database-only hashes
remain cheap to enumerate. The React SPA also needs a ten-minute inactivity
timeout and immediate logout/revocation without persisting bearer tokens in
browser storage.

The product plan requires JWT authorization, but a self-contained JWT alone
cannot provide immediate revocation or rolling server-observed inactivity.

## Decision

### PIN verification

Treat a PIN as a string and enforce four ASCII digits for children or six for
adults. Derive a fixed-length value with HMAC-SHA-256 using a deployment-only
pepper, then store that value with ASP.NET Core
`PasswordHasher<HouseholdMember>`. Configure its versioned PBKDF2 format to at
least 220,000 HMAC-SHA-512 iterations and benchmark verification on
Serendipity. Keep the framework's unique salt and embedded format metadata so
hashes can be upgraded after successful verification.

Store the pepper outside Git and PostgreSQL. Production startup fails when the
pepper is absent. Rate limiting and persisted short lockouts remain mandatory
because hashing cannot make a four-digit secret resistant to online guessing.

### Session boundary

Issue a signed five-minute access JWT after PIN verification. Keep it only in
SPA memory and send it in the `Authorization` header. The JWT contains subject,
role, server-session ID, issuer, audience, issued/expiry times, and token ID.

Store a revocable session in PostgreSQL and check it for every protected
request. Give the browser a rotating 256-bit refresh token in a host-only,
`HttpOnly`, `SameSite=Strict` cookie scoped to `/api/auth`; persist only its
hash. Refresh is allowed only inside the ten-minute server-observed inactivity
window. Reuse of a rotated token revokes the session. Logout revokes the
session and clears the cookie, making an otherwise unexpired JWT unusable.

Refresh and logout are same-origin POST operations which validate the request
Origin. No CORS origin is enabled. Use `Secure` cookies for HTTPS origins. The
current HTTP-only LAN deployment is an explicit residual risk and must never be
made internet-accessible.

## Alternatives considered

### Fast PIN digest or encryption

Rejected. A fast digest makes exhaustive guessing cheap, while encryption is
reversible and unnecessary for verification.

### Password hash without a pepper

Rejected for this low-entropy input. A stolen household database contains only
10,000 possible child PINs; keeping a pepper outside the database materially
improves database-only compromise resistance.

### Full ASP.NET Core Identity UI and schema

Rejected for the first slice. The application has a small existing household
model and same-origin SPA; importing Identity's user, role, recovery, and UI
model would add unused concepts. Reuse the supported password hasher while
keeping feature-owned Identity tables and explicit policies.

### Cookie-only authentication

Rejected because the accepted plan requires JWT-authorized API calls. It would
also make every application write endpoint depend directly on ambient cookie
and anti-forgery behavior.

### JWT stored in local or session storage

Rejected. Persistent browser storage unnecessarily exposes the bearer token to
later script execution and leaves stale identity state after logout or expiry.

### Stateless access and refresh JWTs

Rejected. They cannot provide immediate logout, refresh-token replay detection,
or the required rolling inactivity boundary without server state.

## Consequences

### Positive

- Database theft alone does not reveal enough material to enumerate PINs.
- Versioned hashes can increase their work factor after successful sign-in.
- Access JWTs remain short-lived and absent from persistent browser storage.
- Server sessions provide immediate revocation, rotation, replay detection, and
  an exact inactivity rule.
- Adult/child authorization uses validated claims plus live session state.

### Negative

- Authentication adds two production secrets and a server-session database
  read for protected requests.
- Losing or rotating the PIN pepper requires resetting all PINs.
- Session activity and refresh rotation introduce writes and concurrency cases.
- Plain HTTP permits a hostile LAN participant to observe traffic; this remains
  unacceptable for public exposure.

### Neutral

- This does not adopt the full ASP.NET Core Identity data model or UI.
- Sessions do not survive database loss, which is consistent with their role as
  revocable state.
- An absolute maximum session age is not added in the MVP; inactivity and
  explicit logout control lifetime.

## References

- [Microsoft: Hash passwords in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/password-hashing?view=aspnetcore-10.0)
- [Microsoft: Configure JWT bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [OWASP: Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
