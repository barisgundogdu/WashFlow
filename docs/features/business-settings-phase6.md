# Business profile and weekly hours — Phase 6

Implemented for an existing Business/tenant. Traceability: SRS FR-01/02/03 and NFR tenant
isolation, authorization and consistency requirements; foundations for FR-07/08/21 scheduling
and discovery only, without implementing them. The Phase 6 user decision confirms each physical
branch is an independent Business, a user can own several Businesses, and one membership can have
Owner and Employee grants. Existing onboarding creates the Business; no second creation flow exists.

## Owner workflow

Owner home → **İşletme ayarları** revalidates `/api/v1/auth/contexts` before opening a settings
route in that Owner business's private navigator. The screen loads actual persisted fields, supports
editing, shows unsaved state, confirms discard before reload/back/switch, blocks duplicate saves,
and performs a new GET after a confirmed save. Returning home reloads contexts so a changed name is
reflected. Invitation management remains independently accessible.

A network/timeout/unknown write outcome never becomes success and is never automatically retried.
A conflict or uncertain save blocks further saves until an explicit authoritative reload; reload
requires discarding the draft. A confirmed save followed by a failed GET remains explicitly confirmed
but permits only read recovery, not replay. The screen supports seven individual day controls,
closed days, time input with minute precision and a separate closing boundary of 24:00.

Logout/account/role/business changes invalidate the scope. App suspension clears tenant navigation;
resume revalidates contexts, returning to the role home. Unsaved settings are intentionally discarded
on suspension/logout for this security boundary, rather than cached across lifecycles. A dispatched
write may still commit after cancellation/navigation; its stale completion cannot populate another
screen or tenant. Dialogs use the tenant navigator and disappear with the old scope. No role grants
or settings drafts are persisted on the client.

## API and replacement semantics

Both authenticated endpoints use `Cache-Control: no-store` and the existing authentication rate limit:

- `GET /api/v1/businesses/{businessId}/settings` → 200 `BusinessSettingsView`.
- `POST /api/v1/businesses/{businessId}/settings` → 200 updated view; **full replacement**, not a patch.

View = `businessId` + all input fields below. Identity is the authorized route ID, never a body ID.
Input:

| Field | Type/limit |
| --- | --- |
| `version` | Required positive uint32 PostgreSQL xmin from last GET |
| `name` | Required, nonblank, maximum 200 characters |
| `phone` | Optional public-facing contact string, maximum 40; ASCII digits and common phone punctuation |
| `contactEmail` | Optional address, maximum 320, no display-name syntax |
| `addressLine1`, `addressLine2` | Optional, maximum 250 each |
| `district`, `city`, `region` | Optional, maximum 100 each |
| `postalCode` | Optional, maximum 32; no country-specific format assumed |
| `countryCode` | Optional two ASCII letters, normalized uppercase; format validation, not a country-registry lookup |
| `description` | Optional maximum 2000; plain text with line breaks allowed |
| `timeZoneId` | Optional IANA ID, maximum 100; required when hours are configured |
| `weeklyHours` | null (unconfigured), or exactly seven day objects |

Strings are trimmed; empty optional values become null. Control characters are rejected except
line breaks/tabs in description. Optional details can remain unknown; no fake phone, address, timezone
or schedule is assigned to existing businesses. These endpoints are private Owner settings; the
public-facing phone is not yet published by a discovery endpoint. Publication/privacy policy for
other contact fields remains future work.

A day object is `{day, closed, opensAtMinute, closesAtMinute}`. ISO weekdays Monday=1 to Sunday=7,
each exactly once; response order is ascending. Closed=true requires both minute values null.
Open days require integer `0 <= opensAtMinute < closesAtMinute <= 1440`. Opening 00:00 is 0;
closing 24:00 is 1440, an exclusive end boundary. Equal boundaries are invalid, so a full day must
be explicitly 0–1440. Only one same-day interval is supported; no overnight intervals, split shifts,
seasonal schedules or date exceptions. Disabling configuration sends null rather than inventing
seven closed days. Turning it on creates an editable, unsaved seven-closed-day draft; only explicit
Owner save persists it.

Times are local wall-clock minutes in the Business timezone, not UTC instants. IANA IDs are checked
against the server's .NET/ICU timezone mapping (`Etc/UTC` is explicitly supported); Windows timezone
names are rejected. No tenant is assumed to operate in Istanbul. Future appointment code must resolve
DST gaps/ambiguities and date exceptions under approved booking policies; storing this weekly pattern
does not itself make scheduling safe. The host must provide current timezone data (normal Linux/macOS
ICU/IANA support or the equivalent .NET configuration on deployment).

Errors: malformed JSON/types/field/schedule errors 400; unauthenticated or invalid session 401;
no current Owner grant (including unknown business) 403; stale version 409 `settingsConflict`;
rate limit 429. Error messages in Flutter are safe Turkish strings, never raw provider/response text.
No real contact ownership verification, geocoding or phone delivery is implied.

## Authorization and concurrency

Every read/write transaction locks the route Business first, then the actor's membership and Owner
grant, using the same order as employee invitations. Active Owner access to that exact Business is
required. Employee-only, Customer-only, revoked, and foreign-business Owner access is denied. A member
with both grants can use these endpoints because the backend confirms Owner; an Employee-selected
Flutter experience still has no settings destination. No membership or grant is changed by this feature.

The Business's established PostgreSQL `xmin` optimistic version is returned as `version`. After the
business/authorization locks, updates compare the submitted version; stale requests return 409 without
changes. Profile and all seven hours commit atomically, advancing the parent xmin even for hours-only
or identical requests. Two competing Owner edits cannot silently overwrite each other. The client
must never refresh a version and automatically replay an old draft. Versions are opaque snapshots,
not timestamps, grants or durable identifiers; reload after conflicts/uncertain responses.

## Migration and deployment safety

`20261027071455_BusinessProfileAndWeeklyHours` adds nullable fields to `Businesses`, a false-default
`HoursConfigured` flag and `WeeklyOpeningDays`. Composite PK `(BusinessId, Day)`, a restricted Business
FK, weekday bounds and closed/open interval checks protect persisted rows. Application validation and
the transaction enforce complete seven-day configurations. Generated upgrade SQL was reviewed offline:
only additive profile columns/table/constraints and migration history; no existing row details are
fabricated or removed. A dedicated upgrade test inserts a legacy Business at the previous migration,
applies the new migration, and checks name preservation and unset fields.

No migration was applied to an existing development/production database. Tests use a separate
`washflow-phase6-test` PostgreSQL container with tmpfs storage on a separate loopback port; fixtures
create/drop only unique disposable schemas. The live API's public schema is also in that disposable
database. The schema upgrade test creates and drops only its own uniquely named schema there.

Before deploying: back up and review generated SQL on a copy; arrange a controlled migration window
and deploy this backend/mobile combination together. Business entity reads now expect the new columns,
so do not deploy the new binary against an unmigrated database. Apply the migration only under explicit
deployment authorization. Rollback drops profile columns and weekly hours and loses those settings;
restore from backup if needed. No migration/deployment command should target live data during testing.

## Verification and limitations

Final executed results and the changed-file inventory follow at the end of this document.

Automated backend evidence includes real endpoint/PostgreSQL tests for profile/hours persistence,
null/unconfigured and closed-day semantics, anonymous/Employee/Customer/foreign Owner denial,
multi-business Owner separation, removed grants, concurrent edits and a legacy-schema upgrade.
Existing onboarding, verification, sessions, context and invitation tests remain part of the full suite.

Flutter tests cover requests, bearer headers, parsing, UTF-8 data, safe HTTP error categories,
network/timeouts/no write retry, editing, validation, seven-day controls, duplicate saves, conflict
and uncertain outcomes, failed post-save GET, scoped responses, pending reads/writes during switches,
revoked access, forged root routes, dialogs, lifecycle suspension, back-discard and compact/large text.
The opt-in simulator harness uses actual adapters, UI and iOS Keychain against Kestrel/PostgreSQL.
It constructs `WashFlowApp` with injected services; it does not validate app-store bootstrap/signing.

Run ordinary Flutter checks from `mobile`. For the live scenario use only the separately isolated
Development backend on loopback port 5189, with Memory email and private, ephemeral signing material:

```sh
flutter test integration_test/business_settings_live_test.dart -d <ios-simulator-id> \
  --dart-define=PHASE5_API_URL=http://127.0.0.1:5189
```

The existing Phase 5 fixture/helper and define are reused; this introduces no production email setup.
The URL guard is a reminder, not proof of database isolation. Verify the backend's target privately
before running. Do not echo connection strings or test credentials, capture sensitive screens, or
run alongside a personal app session; integration tests clear the app's test Keychain session.

Manual follow-up: physical iOS/Android keyboard and accessibility checks; HTTPS/signed-release build;
edit/discard on OS backgrounding; deliberate connection loss after save and GET-only recovery;
two physical Owner sessions conflicting on the same business; approve any future overnight/split-shift,
DST booking and public-contact exposure policies before implementing them. No Android, physical-device,
public discovery, appointment scheduling or production email testing is claimed by this phase.

## Final executed results — 27 October 2026

Baseline branch/HEAD remain `main` / `044fe2ef6cfc453ecf60c868989dfb45fe1b0347`.

| Gate | Result |
| --- | --- |
| `dotnet build backend/WashFlow.sln` | Passed; 0 warnings, 0 errors |
| Backend unit tests | 75 passed, 0 skipped (27 new validation cases) |
| Backend integration tests | 87 passed, 0 skipped: 37 non-PostgreSQL + 50 PostgreSQL (6 new cases) |
| `flutter analyze` | Passed, no issues |
| `flutter test` | 178 passed (38 new service/controller/widget cases) |
| iOS simulator real HTTP/Kestrel/PostgreSQL | 3 passed together: settings + both existing invitation scenarios |
| `git diff --check` | Passed |

The settings simulator case edits actual profile fields, configures Monday 09:30–24:00 with the other
six days closed, saves and independently reads persisted fields/hours, provokes a stale-version 409,
deliberately discards/reloads the winning state, returns to Owner home, opens invitation management
and logs out. These values are synthetic test data in the disposable database, never defaults for a
real business. The combined live run completed in 1 minute 41 seconds including two simulator builds.

The PostgreSQL upgrade test ran the prior migration, inserted a legacy business row, applied the new
migration and checked preserved name, null contact/address/timezone and no invented day rows. The
concurrency test sent two competing HTTP updates with the same version: exactly one 200 and one 409.
No security test was disabled or weakened. Existing model table/migration count assertions were updated
for the additional table and fourth migration while retaining model-drift and constraint checks.

The generated SQL is preserved for review at
[BusinessProfileAndWeeklyHours.sql](../../backend/docs/migrations/20261027071455_BusinessProfileAndWeeklyHours.sql).
Only the isolated disposable test database was migrated. No existing development/production database
was accessed or modified. The dedicated backend and `washflow-phase6-test` container were stopped and
removed after validation. There was no commit or push.

This phase is ready for code review and a controlled demo with an explicitly migrated test backend.
Deployment migration approval, physical iOS/Android checks, signed release/HTTPS checks and future
scheduling/discovery policies remain outside this phase. No remaining implementation blocker was found.

## Changed/new files

Total: 36 files.

- `backend/README.md`
- `backend/docs/migrations/20261027071455_BusinessProfileAndWeeklyHours.sql`
- `backend/src/WashFlow.Api/Features/Businesses/BusinessSettingsEndpoints.cs`
- `backend/src/WashFlow.Api/Program.cs`
- `backend/src/WashFlow.Application/Businesses/BusinessSettings.cs`
- `backend/src/WashFlow.Domain/Businesses/Business.cs`
- `backend/src/WashFlow.Domain/Businesses/WeeklyOpeningDay.cs`
- `backend/src/WashFlow.Infrastructure/Businesses/EfBusinessSettingsStore.cs`
- `backend/src/WashFlow.Infrastructure/DependencyInjection.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Configurations/BusinessConfiguration.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261027071455_BusinessProfileAndWeeklyHours.Designer.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261027071455_BusinessProfileAndWeeklyHours.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/WashFlowDbContextModelSnapshot.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/WashFlowDbContext.cs`
- `backend/tests/WashFlow.IntegrationTests/BusinessSettingsPostgresTests.cs`
- `backend/tests/WashFlow.IntegrationTests/PersistenceModelTests.cs`
- `backend/tests/WashFlow.UnitTests/BusinessSettingsValidationTests.cs`
- `backend/tests/WashFlow.UnitTests/IdentityModelTests.cs`
- `backend/tests/WashFlow.UnitTests/InfrastructureTests.cs`
- `docs/features/business-settings-phase6.md`
- `docs/requirements/WashFlow_SRS_v0.2.md`
- `mobile/README.md`
- `mobile/integration_test/business_settings_live_test.dart`
- `mobile/lib/app/app.dart`
- `mobile/lib/app/routing/app_router.dart`
- `mobile/lib/features/auth/presentation/session_screen.dart`
- `mobile/lib/features/business_settings/data/api_business_settings_service.dart`
- `mobile/lib/features/business_settings/domain/business_settings.dart`
- `mobile/lib/features/business_settings/presentation/business_settings_controller.dart`
- `mobile/lib/features/business_settings/presentation/business_settings_screen.dart`
- `mobile/lib/features/owner/presentation/owner_home_screen.dart`
- `mobile/lib/main.dart`
- `mobile/test/business_settings_controller_test.dart`
- `mobile/test/business_settings_service_test.dart`
- `mobile/test/business_settings_widget_test.dart`
- `mobile/test/support/settings_fakes.dart`

Suggested commit (not created): `feat: add Owner business profile and weekly hours management (#2)`.
