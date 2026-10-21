# WashFlow backend foundation — Issue #2

This is Phase 2 of the approved backend plan: a modular monolith with API,
Application, Domain and Infrastructure projects. It is not the full SRS Phase 2
milestone. Authentication, domain entities, tenant authorization, email, Flutter
integration and migrations are not implemented. Issue #2 is not complete.

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
`ConnectionStrings__WashFlow` fails startup with a configuration error. A configured but
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
- Other paths: 404 Problem Details. No registration/login or business endpoints exist.

## Project boundaries and dependency injection

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application
```

`Program.cs` is the composition root and calls `AddApplication` / `AddInfrastructure`.
Application has an intentionally empty registration entry point until use cases exist.
Domain is intentionally empty. Infrastructure registers a scoped `WashFlowDbContext`
using Npgsql and a tagged database readiness check. DbContext discovers future EF
configurations from its own assembly; there are no entities or migrations in this phase.
Do not introduce tenant-owned persistence without the approved authorization/isolation rules.

## Error contract

Unhandled exceptions, empty HTTP error responses and readiness failures use Problem Details
(`application/problem+json`) for JSON clients. Responses contain `type`, `title`, `status`,
semantic `code` and `traceId`. Titles are Turkish. Exception details, stack traces, request
paths and query strings are not returned, including in Development. The developer exception
page is deliberately disabled. Framework logs are server-side; configure restricted log
access/retention and redaction before adding credential-bearing requests. No request-body
logging or sensitive EF logging is enabled. Field validation contracts will be added with
actual request DTOs, rather than speculative domain exceptions.

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

Use a dedicated test database. The current database test runs a read-only `SELECT 1`;
it does not create tables, run migrations or modify records. If the variable is set but
PostgreSQL is unavailable, the test fails. Do not report skipped tests as database verification.

## Approved decisions for later phases (21 October 2026)

These explicit decisions supplement the draft [SRS v0.2](../docs/requirements/WashFlow_SRS_v0.2.md)
and supersede its conflicting open questions. They are not implemented here:

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

## Files created in this phase

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
