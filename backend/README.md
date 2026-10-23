# WashFlow backend — Issue #2

The backend includes the foundation, Phase 3 identity/tenant persistence models and
Phase 4A core authentication APIs and Phase 4B email verification. It remains a modular monolith with API, Application,
Domain and Infrastructure projects. Password reset, employee onboarding, tenant-context
authorization and Flutter integration remain future work.
Issue #2 is not complete. See [core authentication setup and endpoints](docs/core-authentication.md).
The phase numbering here refers to the backend plan, not the broader SRS milestones.

## Prerequisites

- .NET 10 SDK (the backend global.json accepts installed 10.0 feature bands).
- PostgreSQL, a database and a least-privilege login with CONNECT permission.
- NuGet access for the initial restore.

.NET 10 LTS was selected because .NET 8/9 reach end of support on 10 November 2026.
See the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).
The installed system SDKs were 8/9 during initial verification; a temporary .NET 10
SDK was downloaded into `/tmp/washflow-dotnet`. This is not a permanent installation.
Install .NET 10 normally for subsequent development, or put that temporary directory
on PATH while it exists.

## Configure and run

From the repository root, in a local development shell:

```sh
cd backend
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5080
# Example placeholders only; supply your own database and credentials.
export ConnectionStrings__WashFlow='Host=localhost;Port=5432;Database=washflow;Username=washflow;Password=REPLACE_LOCALLY'
dotnet restore WashFlow.sln
dotnet build WashFlow.sln --no-restore
dotnet run --project src/WashFlow.Api --no-build
```

Environment variables override appsettings.json. ASP.NET Core also accepts command-line
configuration and its standard Development user-secrets provider. Do not commit credentials
or pass secrets on command lines. No `.env` loader is installed. Missing/blank
`ConnectionStrings__WashFlow` fails startup with a configuration error. JWT configuration is
also mandatory: set `Jwt__Issuer`, `Jwt__Audience` and `Jwt__SigningKey` as documented in the
[authentication setup](docs/core-authentication.md). A configured but
unreachable database allows liveness while readiness fails. No connection is opened during
DI registration; no database is created, migrated or seeded automatically.

PostgreSQL must already be running, and the configured database must exist. For example,
a PostgreSQL administrator can create a dedicated login/database using their normal
provisioning tools. Use a separate migration identity when schema work is introduced.
For remote production databases configure certificate-validated TLS, such as
`SSL Mode=VerifyFull`, with the appropriate trust roots. Production hosting must terminate
HTTPS and set `AllowedHosts` to the actual API hostname(s); only localhost/127.0.0.1 are
allowed by the checked-in defaults. Local HTTP is for development only.

```sh
curl -i http://localhost:5080/health
curl -i http://localhost:5080/health/ready
```

- `/health`: process liveness, 200 with `{ "status": "healthy" }`; never queries PostgreSQL.
- `/health/ready`: verifies PostgreSQL connectivity through EF Core; 200 when reachable,
  otherwise 503 with safe Problem Details. It does not verify schema/migrations yet.
- Core authentication: see [Phase 4A endpoints](docs/core-authentication.md).
- Other paths: 404 Problem Details. No business-operation endpoints exist.

## Project boundaries and dependency injection

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application
```

`Program.cs` is the composition root and calls `AddApplication` / `AddInfrastructure`.
Application registers AuthService and owns validation/use cases and infrastructure interfaces.
Domain holds platform identities, independent customer profiles, businesses, memberships,
role grants and security credential records without EF dependencies. Infrastructure registers
a scoped `WashFlowDbContext` using Npgsql and a tagged database readiness check. DbContext
loads EF configurations from its own assembly. Infrastructure implements password hashing,
token issuance, session-aware bearer validation and transactional persistence.
The schema preserves tenant relationships; it does not itself authorize tenant access.
No tenant data endpoints exist. Future handlers must check membership/roles and scope every
read/write; DbSets are not a public or authorization-filtered repository.

## Error contract

Unhandled exceptions, empty HTTP error responses and readiness failures use Problem Details
(`application/problem+json`) for JSON clients. Responses contain `type`, `title`, `status`,
semantic `code` and `traceId`. Titles are Turkish. Exception details, stack traces, request
paths and query strings are not returned, including in Development. The developer exception
page is deliberately disabled. Framework logs are server-side; configure restricted log
access/retention and redaction before adding credential-bearing requests. No request-body
logging or sensitive EF logging is enabled. Authentication DTOs now return Turkish field
validation errors, with generic responses for sensitive account/credential failures.

## Tests

```sh
cd backend # omit if already here
dotnet test WashFlow.sln --no-build
```

Unit tests verify missing configuration, Npgsql selection and scoped context lifetime.
HTTP integration tests use ASP.NET Core TestServer with an intentionally unreachable local
PostgreSQL port. They verify liveness, 404/500 safe errors, and unavailable database readiness.
The exception injection middleware exists only in the test project.
These HTTP tests do not prove successful PostgreSQL connectivity.

The real PostgreSQL test is explicitly skipped unless a connection is supplied:

```sh
export WASHFLOW_TEST_POSTGRES='Host=localhost;Port=5432;Database=washflow_test;Username=washflow_test;Password=REPLACE_LOCALLY'
dotnet test WashFlow.sln --no-build --filter Category=PostgreSQL
```

Use a dedicated test database. The connectivity test runs a read-only `SELECT 1`.
Phase 3 persistence tests create a uniquely named `washflow_test_<guid>` schema, migrate
that schema and remove only that schema on fixture disposal. The test database login needs
CONNECT and CREATE on the dedicated database; it does not need database-creation permission.
No `EnsureCreated`, database deletion or shared application-schema migration is performed.
Most tests roll back their rows; the concurrency test commits data only inside the disposable
schema. If the variable is set but PostgreSQL is unavailable, tests fail. Do not report skipped
tests as database verification. A terminated test process may leave a prefixed test schema for
manual cleanup; never point this test suite at a production database.

## Approved decisions for later phases (21 October 2026)

These explicit decisions supplement the draft [SRS v0.2](../docs/requirements/WashFlow_SRS_v0.2.md)
and supersede its conflicting open questions. Persistence support is now present where
noted below; the authentication/onboarding/session flows are not implemented:

- Business onboarding is self-service; no platform approval. Customer and owner access
  requires account email verification. Initially collect business name, owner full name,
  email and password; other profile details come later.
- Each physical branch is a separate Business/tenant. A user may own/manage multiple
  businesses and have Customer, Owner and Employee contexts. A membership may have both
  Owner and Employee roles.
- Owners create employee onboarding records. Employees use secure email setup; existing
  users authenticate and explicitly accept membership without duplicate accounts.
- Password reset uses secure expiring email links.
- Sessions persist securely across app restarts. Logout revokes the current device session.
  JWT access lifetime is 15 minutes; session absolute maximum is 30 days, with rotating,
  hashed refresh credentials.
- No Platform Admin role or admin panel in the MVP.

## Traceability and review

Foundation wiring supports Issue #2; safe errors support SRS NFR-07. Configuration and
connectivity checks establish infrastructure for NFR-04. CI, authentication, tenant isolation,
customer privacy and end-to-end mobile evidence remain future work; their acceptance criteria
are not satisfied by this foundation. The SRS draft itself and GitHub issue/board were not
rewritten by this task. Review this phase before proceeding.

## Initial verification — 21 October 2026

- SDK: temporary .NET 10.0.402; all six projects build with zero warnings/errors.
- Unit tests: 4 passed.
- HTTP integration tests: 5 passed (including Development-mode exception safety).
- Real PostgreSQL connectivity: 1 explicitly skipped; `WASHFLOW_TEST_POSTGRES` unset.
- No local listener was found on port 5432; `psql`/`pg_isready` were unavailable and
  the Docker daemon was not running. Successful database connectivity is not verified.
- `git diff --check` passed. No commits or pushes were made.

Verification used `dotnet build --no-restore --disable-build-servers -m:1
-p:UseSharedCompilation=false` and `dotnet test --no-build --no-restore
--disable-build-servers -m:1`, targeting `backend/WashFlow.sln` from the repository root.
CLI/package caches were placed under `/tmp/washflow-cli` and `/tmp/washflow-nuget`.
The test runner required permission to open its local communication socket outside
this session's sandbox. The ordinary parallel build stalled in the sandbox; serial
compilation without build servers succeeded.

## Files created in Phase 2 (historical inventory)

All paths below are relative to `backend/`. The repository root `README.md` was
also updated with a backend setup link. Generated `bin/` and `obj/` files are ignored.

```text
.gitignore
Directory.Build.props
README.md
WashFlow.sln
global.json
src/WashFlow.Api/Errors/ApiExceptionHandler.cs
src/WashFlow.Api/Errors/ApiProblemDetails.cs
src/WashFlow.Api/Program.cs
src/WashFlow.Api/WashFlow.Api.csproj
src/WashFlow.Api/appsettings.json
src/WashFlow.Application/DependencyInjection.cs
src/WashFlow.Application/WashFlow.Application.csproj
src/WashFlow.Domain/WashFlow.Domain.csproj
src/WashFlow.Infrastructure/DependencyInjection.cs
src/WashFlow.Infrastructure/Health/PostgresHealthCheck.cs
src/WashFlow.Infrastructure/Persistence/WashFlowDbContext.cs
src/WashFlow.Infrastructure/WashFlow.Infrastructure.csproj
tests/WashFlow.IntegrationTests/ApiFactory.cs
tests/WashFlow.IntegrationTests/ApiFoundationTests.cs
tests/WashFlow.IntegrationTests/PostgresConnectivityTests.cs
tests/WashFlow.IntegrationTests/WashFlow.IntegrationTests.csproj
tests/WashFlow.UnitTests/InfrastructureTests.cs
tests/WashFlow.UnitTests/WashFlow.UnitTests.csproj
```

## Phase 3 persistence design

| Model | Ownership / constraints |
|---|---|
| User | Platform identity; unique normalized email, encoded password hash, security stamp, nullable email-verification timestamp |
| CustomerProfile | Optional one-to-one User association; no BusinessId and no membership requirement |
| Business | One physical branch / tenant; no approval state or Platform Admin role |
| BusinessMembership | User–Business association, unique per pair, with revocation timestamp |
| BusinessMembershipRole | Composite membership/role key; only Owner=1 and Employee=2; both rows may coexist |
| EmailVerificationToken | User, intended normalized email and security-stamp snapshot; hashed token, expiry, consumption/revocation |
| PasswordResetToken | User and security-stamp snapshot; hashed token, expiry, consumption/revocation |
| EmployeeInvitation | Business, same-business issuing membership, intended normalized email, hashed token, expiry and explicit accepting User |
| DeviceSession | User, security-stamp snapshot, display-only device name, absolute expiry and current-session revocation |
| RefreshToken | Hashed credential, owning session, consumption/revocation and same-session successor reference |

IDs use application-generated UUIDv7, except security stamps which are independent random UUIDs.
All timestamps map to PostgreSQL `timestamp with time zone`; callers must supply UTC offsets.
DbContext sets initial UpdatedAt to CreatedAt, stamps updates and protects CreatedAt from EF
updates. PostgreSQL `xmin` provides optimistic concurrency on mutable entity rows, including
single-use tokens, memberships and sessions. Role grant uniqueness is enforced by its primary key.
All relationships use RESTRICT deletion; account/business erasure requires a separately reviewed
retention/deletion workflow. Empty IDs, blank required strings, invalid token digests, invalid
lifecycles and unsupported role values are rejected by database checks.

Email normalization uses trimmed invariant uppercase, independent of Turkish UI culture. User
construction sets it; future email-change methods must preserve normalization and verification
invalidation. PasswordHash holds an encoded adaptive password-hasher result (including salt/work
factor metadata); this phase does not generate or verify password hashes, and the database cannot
prove that an arbitrary string is a valid password hash. The future identity service must use the
approved ASP.NET Core password hasher and must never pass a password into PasswordHash.

`TokenHash` accepts exactly a SHA-256 hex digest, normalizes it to lowercase and redacts ToString.
There are no plaintext token columns. Future services must generate high-entropy random tokens,
hash them before persistence, and use separate credentials per purpose. SHA-256 here applies to
random security tokens, not passwords. Verification/reset expiry durations and invitation expiry
are deliberately not assigned defaults; only a valid expiry after creation is required.

The database limits each session to 720 hours (30 absolute days) from creation. Refresh records
inherit that session's expiry; rotating a token must never extend the session. There is one
unconsumed/unrevoked token per session, and a successor cannot belong to another session or have
multiple predecessors. Later rotation must run in a transaction: consume the old record with its
xmin concurrency check, save, insert the replacement, then link the old record to the replacement
and commit. On concurrency/uniqueness failure the entire transaction must roll back. Reuse/replay
handling, credential checks, expiry checks and session revocation on protected requests remain
future service behavior. The schema alone does not implement login/logout or prevent credential use.

Invitations never create accounts or memberships automatically. The composite issuer FK prevents
cross-tenant references; a later handler must additionally check the issuer's active Owner role.
The accepting account and timestamp must both be present or both absent. Authentication, email
matching, explicit acceptance, account setup and transactional membership creation remain later
work. Customer profiles are not linked to tenant-local walk-in records by email/name/plate.

## Generate, review and apply migrations

From `backend/`:

```sh
dotnet tool restore
dotnet restore WashFlow.sln
dotnet build WashFlow.sln
dotnet ef migrations has-pending-model-changes --project src/WashFlow.Infrastructure --startup-project src/WashFlow.Infrastructure --no-build
dotnet ef migrations script --idempotent --project src/WashFlow.Infrastructure --startup-project src/WashFlow.Infrastructure --no-build --output /tmp/washflow-identity.sql
```

The pinned local EF CLI and EF Design package are development-time migration dependencies.
`WashFlowDbContextFactory` allows these offline commands without a connection string and reads
only `ConnectionStrings__WashFlow` for database commands. It does not read Terminal environments
from other processes, user-secrets or `.env` files. API runtime configuration remains unchanged.

After review, from your own Terminal with the connection already set:

```sh
# Required environment variable: ConnectionStrings__WashFlow
# Must point to the intended local database with schema-creation privileges.
dotnet ef database update --project src/WashFlow.Infrastructure --startup-project src/WashFlow.Infrastructure --no-build
```

The initial migration is `20261021123220_InitialIdentityAndTenancy`. Review its Up method or the
idempotent SQL before applying. API startup still never migrates automatically. This phase's
migration was generated and reviewed offline; applying it to your local PostgreSQL database is
intentionally left to you. No connection to your PostgreSQL instance was attempted by the implementation work.

The added tests cover culture-independent normalization, hash format/redaction, role scope,
concurrency/relationship metadata, offline migration/model consistency, and (when explicitly
configured) migration application/idempotence, unique emails/memberships/profiles, tenant issuer
integrity, restricted deletion, token expiry/session lifetime, refresh rotation and concurrency.
The remaining Issue #2 acceptance criteria require later authentication/authorization flows.

## Phase 3 verification and changed files — 21 October 2026

- `dotnet build`: succeeded, zero warnings/errors.
- `dotnet test`: 12 unit tests and 5 HTTP integration tests passed; no failures.
- 14 PostgreSQL tests (13 persistence/migration cases plus the existing connectivity case)
  explicitly skipped because `WASHFLOW_TEST_POSTGRES` is unset in this session.
- Offline migration/model consistency and idempotent SQL generation passed. The SQL contains
  ten tables, RESTRICT relationships, the refresh-token partial unique index and session cap.
- The local PostgreSQL 17 Docker instance was reported as previously verified by the user.
  This phase does not claim that its new migration/database tests passed against that instance.
  Per the user's instruction, neither the live migration nor credential-dependent tests were run.
- `git diff --check` and whitespace checks on new source files passed. No commits or pushes.

Build/test commands used the same serial/no-build-server options documented in the initial
verification. Only EF Core Design and the pinned local EF CLI were added; Domain still has
no external package dependencies. No API, Application or Flutter behavior was added.

Modified files (repository-relative):

```text
backend/README.md
backend/src/WashFlow.Infrastructure/Persistence/WashFlowDbContext.cs
backend/src/WashFlow.Infrastructure/WashFlow.Infrastructure.csproj
backend/tests/WashFlow.UnitTests/InfrastructureTests.cs
```

Created files (repository-relative):

```text
backend/.config/dotnet-tools.json
backend/src/WashFlow.Domain/Businesses/Business.cs
backend/src/WashFlow.Domain/Businesses/BusinessMembership.cs
backend/src/WashFlow.Domain/Businesses/BusinessMembershipRole.cs
backend/src/WashFlow.Domain/Businesses/EmployeeInvitation.cs
backend/src/WashFlow.Domain/Businesses/MembershipRole.cs
backend/src/WashFlow.Domain/Common/IEntity.cs
backend/src/WashFlow.Domain/Customers/CustomerProfile.cs
backend/src/WashFlow.Domain/Identity/DeviceSession.cs
backend/src/WashFlow.Domain/Identity/EmailVerificationToken.cs
backend/src/WashFlow.Domain/Identity/PasswordResetToken.cs
backend/src/WashFlow.Domain/Identity/RefreshToken.cs
backend/src/WashFlow.Domain/Identity/TokenHash.cs
backend/src/WashFlow.Domain/Identity/User.cs
backend/src/WashFlow.Infrastructure/Persistence/Configurations/BusinessConfiguration.cs
backend/src/WashFlow.Infrastructure/Persistence/Configurations/IdentityConfiguration.cs
backend/src/WashFlow.Infrastructure/Persistence/Configurations/MappingRules.cs
backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261021123220_InitialIdentityAndTenancy.Designer.cs
backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261021123220_InitialIdentityAndTenancy.cs
backend/src/WashFlow.Infrastructure/Persistence/Migrations/WashFlowDbContextModelSnapshot.cs
backend/src/WashFlow.Infrastructure/Persistence/WashFlowDbContextFactory.cs
backend/tests/WashFlow.IntegrationTests/PersistenceModelTests.cs
backend/tests/WashFlow.IntegrationTests/PostgresModelFixture.cs
backend/tests/WashFlow.UnitTests/IdentityModelTests.cs
```

Remaining verification: after reviewing and applying the migration from your Terminal, set
`WASHFLOW_TEST_POSTGRES` to a dedicated test database and run:

```sh
dotnet test WashFlow.sln --no-build --filter Category=PostgreSQL
```

No new onboarding/approval policies were introduced. Verification/reset/invitation expiry
windows, retention/erasure rules and subsequent authentication/authorization workflows remain
for their respective implementation phases. The existing approved 15-minute JWT lifetime
is not implemented or tested in this persistence phase.

## Phase 4A — core authentication

Implemented endpoints and security behavior are documented in
[docs/core-authentication.md](docs/core-authentication.md). Existing Phase 3 design and test
sections above describe their original milestone; Phase 4A now implements the password,
registration, JWT, refresh and logout behavior they previously marked as future work.
No schema change or new migration is required. The existing initial migration must already be
applied to the target database. No database reset or live migration was performed in this phase.

Phase 4A verification (21 October 2026): build passed with zero warnings/errors; 24 unit
and 11 HTTP integration tests passed. Twenty PostgreSQL tests explicitly skipped because
`WASHFLOW_TEST_POSTGRES` is unset (six new authentication tests plus fourteen existing cases).
These results do not claim successful live database registration/rotation/concurrency testing.
The existing local database was not changed. Offline EF model comparison reported no pending
model changes. `git diff --check` and new-file whitespace checks passed. No commit or push.

Tests were run with the documented serial/no-build-server options and a temporary CLI/package
cache. Test execution required localhost socket access outside the sandbox. Remaining local
verification: run the PostgreSQL category from your own configured Terminal. Newly registered
accounts remain blocked from login until the separately planned verification flow is implemented.

Modified Phase 4A files (repository-relative):

```text
backend/README.md
backend/src/WashFlow.Api/Errors/ApiExceptionHandler.cs
backend/src/WashFlow.Api/Errors/ApiProblemDetails.cs
backend/src/WashFlow.Api/Program.cs
backend/src/WashFlow.Application/DependencyInjection.cs
backend/src/WashFlow.Domain/Identity/User.cs
backend/src/WashFlow.Infrastructure/WashFlow.Infrastructure.csproj
backend/tests/WashFlow.IntegrationTests/ApiFactory.cs
```

Created Phase 4A files (repository-relative):

```text
backend/docs/core-authentication.md
backend/src/WashFlow.Api/Features/Authentication/AuthEndpoints.cs
backend/src/WashFlow.Application/Authentication/AuthContracts.cs
backend/src/WashFlow.Application/Authentication/AuthService.cs
backend/src/WashFlow.Application/Authentication/AuthValidation.cs
backend/src/WashFlow.Infrastructure/Authentication/AccessTokenIssuer.cs
backend/src/WashFlow.Infrastructure/Authentication/DependencyInjection.cs
backend/src/WashFlow.Infrastructure/Authentication/EfAuthStore.cs
backend/src/WashFlow.Infrastructure/Authentication/JwtSettings.cs
backend/src/WashFlow.Infrastructure/Authentication/PasswordService.cs
backend/src/WashFlow.Infrastructure/Authentication/RefreshTokenGenerator.cs
backend/tests/WashFlow.IntegrationTests/AuthHttpTests.cs
backend/tests/WashFlow.IntegrationTests/BearerAuthenticationTests.cs
backend/tests/WashFlow.IntegrationTests/CoreAuthenticationPostgresTests.cs
backend/tests/WashFlow.UnitTests/CoreAuthenticationTests.cs
```

## Phase 4B — Email verification

[Authentication documentation](docs/core-authentication.md) now includes email verification/resend
endpoints, delivery providers, security settings, recovery limitations and migration commands.
New registrations persist hashed verification credentials atomically with the account. Verification
is single-use and serialized with resend by a PostgreSQL User-row lock; no tenant grants are added.

Apply `20261021135215_EmailVerificationIssuanceIndexes` after review. It enforces one pending token
per account and indexes cooldown lookup, retaining only the newest legacy pending credential.
Production requires `Email__Provider=Smtp`, `Email__Smtp__Host` and `Email__Smtp__From`, plus optional
SMTP username/password supplied through a secret provider. Development defaults to memory delivery.
Token lifetime/cooldown are configurable using `Verification__LifetimeMinutes` and
`Verification__ResendCooldownSeconds`. No new package dependency was introduced.

Phase 4B verification (21 October 2026):

- Build passed with zero warnings/errors.
- 36 unit tests and 14 HTTP integration tests passed; zero failures.
- 28 PostgreSQL tests explicitly skipped because `WASHFLOW_TEST_POSTGRES` is unset in Codex
  (20 existing cases and 8 new email-verification cases).
- New cases cover token binding/purpose separation, Turkish responses, resend IP throttling,
  account privacy, provider-error redaction, registration/login after verification, reuse,
  concurrent consumption/resend, expired/revoked/stale credentials and legacy migration cleanup.
- Offline idempotent migration SQL generation/review and model consistency passed.
- No live database migration or external SMTP send was performed. Production SMTP delivery
  and credential-dependent database behavior still require local verification.
- `git diff --check` and backend whitespace checks passed. No commits/pushes; Issue #2 unchanged.

Run the PostgreSQL category from your configured Terminal after migration review using the
existing dedicated-schema test setup. Tests create/remove only their uniquely named test schemas,
including an extra isolated schema for initial-to-latest migration-upgrade verification.

Modified Phase 4B files (repository-relative):

```text
backend/README.md
backend/docs/core-authentication.md
backend/src/WashFlow.Api/Features/Authentication/AuthEndpoints.cs
backend/src/WashFlow.Api/Program.cs
backend/src/WashFlow.Application/Authentication/AuthContracts.cs
backend/src/WashFlow.Application/Authentication/AuthService.cs
backend/src/WashFlow.Application/Authentication/AuthValidation.cs
backend/src/WashFlow.Application/DependencyInjection.cs
backend/src/WashFlow.Infrastructure/Authentication/EfAuthStore.cs
backend/src/WashFlow.Infrastructure/Persistence/Configurations/IdentityConfiguration.cs
backend/src/WashFlow.Infrastructure/Persistence/Migrations/WashFlowDbContextModelSnapshot.cs
backend/tests/WashFlow.IntegrationTests/ApiFactory.cs
backend/tests/WashFlow.IntegrationTests/BearerAuthenticationTests.cs
backend/tests/WashFlow.IntegrationTests/PersistenceModelTests.cs
backend/tests/WashFlow.UnitTests/CoreAuthenticationTests.cs
backend/tests/WashFlow.UnitTests/IdentityModelTests.cs
```

Created Phase 4B files (repository-relative):

```text
backend/src/WashFlow.Application/Authentication/EmailVerificationContracts.cs
backend/src/WashFlow.Application/Authentication/EmailVerificationService.cs
backend/src/WashFlow.Infrastructure/Authentication/EfEmailVerificationStore.cs
backend/src/WashFlow.Infrastructure/Authentication/VerificationTokenGenerator.cs
backend/src/WashFlow.Infrastructure/Email/DependencyInjection.cs
backend/src/WashFlow.Infrastructure/Email/DevelopmentEmailSender.cs
backend/src/WashFlow.Infrastructure/Email/SmtpEmailSender.cs
backend/src/WashFlow.Infrastructure/Email/VerificationEmailDispatcher.cs
backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261021135215_EmailVerificationIssuanceIndexes.Designer.cs
backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261021135215_EmailVerificationIssuanceIndexes.cs
backend/tests/WashFlow.IntegrationTests/EmailVerificationHttpTests.cs
backend/tests/WashFlow.IntegrationTests/EmailVerificationPostgresTests.cs
backend/tests/WashFlow.UnitTests/EmailVerificationTests.cs
```
