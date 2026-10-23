# Core authentication and email verification — Issue #2

The API now supports new-customer registration, new-owner/business registration,
verified-account login, refresh rotation, current-session logout and Phase 4B email verification.
Password reset, employee invitation acceptance, authorized context selection, business operations
and Flutter integration remain separate phases.
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

Phase 4B adds the `EmailVerificationIssuanceIndexes` migration. Review and apply it before
running the updated API, using the migration instructions below. It adds no plaintext credential
columns. API startup never creates, resets or migrates databases.
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
| `/email/verify` | token | 200 with Turkish acknowledgement; invalid/expired/used token: 400 |
| `/email/resend` | email | Generic 202 acknowledgement; malformed email: 400; IP limit: 429 |

Registration always creates unverified accounts. A new customer gets a CustomerProfile without
business membership. A new owner gets a new Business (one physical branch), membership and Owner
grant and hashed verification token in one SaveChanges transaction. Customer registration also
persists its hashed verification token in that same transaction. The unique normalized-email constraint protects concurrent
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

New registrations cannot log in until verified. The `/email/verify` endpoint is now the supported
verification path; it requires the secret emailed token and never creates a session or grants
additional roles. Existing Phase 4A tests may arrange verified users directly in disposable schemas;
new Phase 4B tests exercise registration, delivery capture and verification. There is no verification
bypass or public development mailbox endpoint.

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

## Phase 4B — Email verification

Registration generates a separate 256-bit random base64url credential. PostgreSQL stores only its
purpose-separated SHA-256 digest, intended normalized email, user security stamp, creation/expiry
and consumption/revocation timestamps. The raw credential exists only transiently in process memory
and the email delivery path. It is never returned by registration/resend or written to application
logs, files or an outbox table. Refresh tokens cannot be reused as verification tokens.

`POST /api/v1/auth/email/verify` accepts `{ "token": "<emailed credential>" }` in the JSON body.
It is a POST so the credential need not appear in URL/access logs. The endpoint locks the User row,
reads the clock again after obtaining the lock, and checks expiry, consumption, revocation, current
email and security stamp. It updates EmailVerifiedAt and consumes the credential transactionally;
only one concurrent verifier can succeed. Invalid, expired, revoked, stale-email/stamp and used
credentials all return safe Turkish 400 Problem Details with `code: invalidVerificationToken`.
Success returns a Turkish message instructing the user to log in. Existing login/session checks
continue to require verified email; verification does not automatically authenticate the caller.

`POST /api/v1/auth/email/resend` accepts `{ "email": "user@example.com" }`. For syntactically valid
addresses it always returns 202 with `Adres uygunsa doğrulama e-postası gönderilecektir.` whether
the account is unknown, verified, throttled, or eligible. The separate IP limiter permits five
resend requests per minute with no queue; its 429 response does not depend on account existence.
Under the same User row lock, a database-backed per-account cooldown suppresses frequent sends
across API instances. Eligible resend revokes all older pending credentials before saving a new
one. Duplicate registration never resends a token; callers must use this endpoint.

A partial unique index allows at most one unconsumed/unrevoked verification credential per User.
Expired credentials are also revoked during replacement. The additional UserId/CreatedAt index
supports cooldown lookup. The migration retains the newest legacy pending record and revokes any
older pending records before creating the unique index; it does not delete history. Rolling the
migration back restores indexes but does not reactivate revoked credentials.

### Email configuration examples

Development defaults to the bounded in-memory sender. No real email is sent, and the mailbox is
available only through dependency injection (e.g. integration tests or an IDE debugger). No console
logger, public inbox, verification bypass, disk pickup directory or raw-token HTTP endpoint exists.
For manual delivery testing, configure a real STARTTLS SMTP service even in Development.

Production requires explicit SMTP configuration; `Memory` is rejected outside Development:

```sh
export Email__Provider=Smtp
export Email__Smtp__Host=smtp.example.com
export Email__Smtp__Port=587
export Email__Smtp__From=noreply@example.com
# Supply Email__Smtp__Username and Email__Smtp__Password through your secret provider.
# Both may be omitted for a trusted SMTP relay; default OS credentials are never used.
export Verification__LifetimeMinutes=60
export Verification__ResendCooldownSeconds=60
```

The host/from examples are placeholders. Username/password are never hardcoded, printed, or echoed
in configuration errors. SMTP always requires STARTTLS with normal certificate validation; implicit
TLS port 465 is not supported by this adapter. The email is Turkish plain text containing the
credential and its UTC expiry. It does not invent a Flutter/deep-link target: clients submit the
emailed credential to the POST endpoint. Replace `IEmailSender` with another production provider
through infrastructure DI if a non-SMTP provider is needed later; no API key is shipped.

Security defaults are a 60-minute token lifetime and 60-second cooldown. Allowed configurable
ranges are 1–1440 minutes and 1–3600 seconds respectively. Invalid configuration fails startup.
Registration/login JWT and database configuration from Phase 4A remain mandatory.

Delivery is queued **after the database commit** and happens in a hosted worker, avoiding SMTP
latency differences in anonymous responses. The process-local bounded channel holds up to 100
messages; it does not persist raw credentials. Delivery attempts have 10-second timeouts with up
to three attempts, and expired queued messages are discarded. Failure logs contain only generic
messages without provider exceptions, recipients or tokens. The development mailbox retains at
most 100 messages in process memory.

**Delivery limitation:** this is not a durable outbox. A crash between commit and enqueue, a full
queue, process restart, or exhausted retries can lose delivery. The account remains unverified,
the acknowledgement remains generic, and the user can resend after the cooldown. Do not claim
exactly-once delivery. A durable encrypted delivery architecture would require a separate design;
it is not silently implemented by storing plaintext verification credentials.

### Apply the migration after review

From your own configured Terminal, with `ConnectionStrings__WashFlow` pointing to the intended
local database:

```sh
cd backend
dotnet tool restore
dotnet build WashFlow.sln
dotnet ef migrations script --idempotent --project src/WashFlow.Infrastructure --startup-project src/WashFlow.Infrastructure --no-build --output /tmp/washflow-verification.sql
# Review the SQL, then:
dotnet ef database update --project src/WashFlow.Infrastructure --startup-project src/WashFlow.Infrastructure --no-build
```

Migration: `20261021135215_EmailVerificationIssuanceIndexes`. The implementation did not apply it
or modify the existing PostgreSQL instance. Tests use dedicated temporary schemas only when
`WASHFLOW_TEST_POSTGRES` is explicitly configured. SMTP delivery was not exercised against an
external server in Codex; the test sender never transmits actual emails.
