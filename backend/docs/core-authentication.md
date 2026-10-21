# Phase 4A — Core authentication API (Issue #2)

The API now supports new-customer registration, new-owner/business registration,
verified-account login, refresh rotation and current-session logout. Email delivery,
verification-link consumption, password reset, employee invitation acceptance, authorized
context selection, business operations and Flutter integration remain separate phases.
Issue #2 is not complete. Requirements supported here: FR-01, FR-20, NFR-01/02/04/07.
Tenant-context authorization and FR-27 remain subsequent work.

## Local configuration

Use .NET 10 and the existing migrated PostgreSQL database. In the shell that launches the API,
set `ConnectionStrings__WashFlow`, `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__SigningKey`.
The signing key must be Base64 representing at least 32 cryptographically random bytes.
Generate a local secret with `openssl rand -base64 32`, store it in your local secret provider,
and never commit it. No default signing key is shipped. Missing/invalid JWT configuration fails
startup with a safe error. Issuer and audience must match the intended API/mobile deployment.
These settings use the API's standard environment/user-secrets configuration providers.

From `backend/`:

```sh
dotnet restore WashFlow.sln
dotnet build WashFlow.sln
dotnet run --project src/WashFlow.Api
```

No new migration is required: Phase 4A reuses the Phase 3 schema. Apply the existing initial
migration only if the target database has not received it; use the migration instructions in
[the backend README](../README.md). API startup never creates, resets or migrates databases.
Use HTTPS in deployment and platform-secure token storage when Flutter integration is added.
Development HTTP settings are described in the README. Keep EF sensitive-data logging and
Npgsql detailed database errors disabled for authentication requests.

## Endpoints

All endpoints are POST under `/api/v1/auth`. JSON uses camelCase.

| Endpoint | Request fields | Success |
|---|---|---|
| `/customers/register` | fullName, email, password | 202 with generic message |
| `/businesses/register` | businessName, ownerFullName, email, password | 202 with the same generic message |
| `/login` | email, password, optional deviceName | 200 with token pair |
| `/refresh` | refreshToken | 200 with replacement token pair |
| `/logout` | No body; Authorization: Bearer access token | 204 |

Registration always creates unverified accounts. A new customer gets a CustomerProfile without
business membership. A new owner gets a new Business (one physical branch), membership and Owner
grant in one SaveChanges transaction. The unique normalized-email constraint protects concurrent
registration. Duplicate emails receive the same 202 acknowledgement and do not create a business,
change passwords, add Customer profiles or attach memberships to an existing account. Authenticated
onboarding of additional businesses for existing users is a later use case; this anonymous endpoint
cannot be used for account linking. Client-supplied roles, business IDs and verification flags grant
nothing; only the endpoint's fixed server-side registration operation determines the new records.

Acknowledgement:

```json
{"message":"Kayıt talebiniz alındı. Erişim için e-posta doğrulaması gereklidir."}
```

Token response fields are `accessToken`, `refreshToken`, `accessTokenExpiresAt`,
`sessionExpiresAt` and `tokenType` (Bearer). Dates are UTC. These are credentials: do not log them.
No record-generated ToString methods include request passwords or response credentials.
Responses containing credentials have Cache-Control: no-store and Pragma: no-cache.

Names are required and limited to 200 characters; email is trimmed, syntax checked and limited
to 320 characters. Passwords are preserved exactly and bounded at 1024 characters, with blank
values rejected. This phase does not invent a minimum length/complexity policy beyond the existing
required-field rule. The password-strength product policy remains to be confirmed. Device name is
optional, display-only and limited to 200 characters; absent/blank names become “Mobil cihaz”.
Each login creates its own device session; a device name does not identify or authorize a device.

Validation errors use 400 Turkish Problem Details with `code: validation` and an `errors` dictionary.
Unknown accounts, wrong passwords, unverified accounts and invalid refresh credentials return 401
without an account-existence or verification-status distinction. Invalid login/refresh results use
`code: invalidCredentials`; failed bearer authentication uses `code: unauthenticated`.
Malformed JSON also returns safe Turkish 400 errors. The API does not return raw server exception
messages, passwords, database details or refresh-token state. Existing safe 404/500/503 behavior
is preserved. A basic per-process, per-peer-IP limit allows 60 authentication requests per minute
and returns 429 without queuing. Configure trusted proxy forwarding/rate controls at deployment;
untrusted forwarding headers are not used to assign identity or bypass the limiter.

## Security and persistence

- ASP.NET Core Identity PasswordHasher provides salted, versioned PBKDF2 hashes (210,000 iterations).
  Login verifies against a dummy hash when the account is absent. Successful verification rehashes
  outdated hash formats/work factors without changing the credential security stamp.
- JWTs use HS256, configured issuer/audience/key and no clock-skew allowance. Signature, allowed
  algorithm, issuer, audience and expiry are validated. Access lifetime is 15 minutes, shortened
  when the absolute session deadline is closer. Claims contain only sub, sid and jti, plus JWT
  protocol claims; there are no global Owner/Employee or client-selected tenant grants.
- Every authenticated request checks the referenced session and account in PostgreSQL: matching
  user, unrevoked/unexpired session, verified email and matching security stamp. Logout therefore
  invalidates previously issued JWTs for that session immediately. An unavailable database fails
  closed through the safe error pipeline.
- Refresh tokens contain 32 random bytes encoded as base64url. Only their SHA-256 digests are stored.
  Sessions expire absolutely after 30 days; refreshing never extends that deadline. Creation times
  are normalized to PostgreSQL microsecond precision so reloaded expiry values remain stable.
- Refresh/logout acquire a PostgreSQL row lock on the device session in a transaction. Rotation
  consumes the old record, inserts the successor and links the history before committing. Reusing
  a consumed/revoked refresh token revokes that session and its refresh credentials. Concurrent
  refresh reuse may yield one successful response followed by family revocation; clients must
  serialize refresh calls and authenticate again if a replay/reuse is detected. Other sessions
  belonging to the same user are unaffected.
- Logout takes user/session identifiers only from the validated JWT and changes only that session.
  A second logout with its revoked JWT receives 401. There is no all-device logout in this phase.
- Tenant operations and role/context selection are not exposed. Future business endpoints must
  independently validate active membership and role against their specific tenant; a valid JWT
  alone does not authorize any business data.

## Verification boundary

New registrations cannot log in until verified. No production verification shortcut or email-token
consumption endpoint is added here. Automated tests arrange verified users directly in disposable
schemas; do not bypass verification in the running application to demonstrate login.

```sh
dotnet test WashFlow.sln
# With WASHFLOW_TEST_POSTGRES set to a dedicated test database:
dotnet test WashFlow.sln --filter Category=PostgreSQL
```

PostgreSQL tests use the existing disposable-schema fixture and never migrate/reset the shared
application schema. Added cases cover unverified registration, Owner-only tenant grants,
customer independence, duplicate/concurrent registration, generic login failures, refresh hashing,
rotation/replay/concurrency and current-device logout. Without the test environment variable these
cases explicitly skip. Passing offline/middleware tests is not a claim of successful database
integration testing.
