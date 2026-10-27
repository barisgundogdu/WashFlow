# Owner scheduling HTTP API — Phase 9C

Traceability: SRS v0.2 FR-07/08/24, tenant/ownership rules and NFR-07; Issues #4/#5/#11.
Continues [Phase 9A](scheduling-domain-phase9a.md) and
[Phase 9B](scheduling-persistence-phase9b.md). This phase exposes owner-side scheduling only;
it does not complete customer booking, Flutter, Phase 9 as a whole, or any GitHub issue.

Baseline: clean `feature/phase9-scheduling` at `040769b`. HEAD remains unchanged. Changes are
uncommitted; no push, issue closure, existing-file deletion or board update was performed.

## Routing and authorization

Base path `B = /api/v1/businesses/{businessId}`. UUID route parameters follow the existing
Minimal API convention. All 19 operations require a valid Bearer JWT session and a **current
Owner grant in that business**. Employee-only and Customer accounts receive 403. The business
ID selects the context; it is never accepted as proof of authorization. Actor and session IDs
come exclusively from the authenticated `sub` and `sid` claims, not JSON/query parameters.

The endpoint group uses the existing `BusinessAuthorizationService` to check active membership
and Owner grant at the API boundary. `EfSchedulingService` independently rechecks session,
security stamp, membership and grant after acquiring its PostgreSQL transaction lock. Revocation
between the API check and transaction is therefore rejected. JWT middleware also validates the
current persistent session. There is no new global Owner claim or client-trusted role flag.

Every endpoint delegates to `ISchedulingService`; endpoints contain no DbContext, SQL, locks or
capacity calculations. New service methods for service/category requirement listing, withdrawing
a requirement's single-stage confirmation, and bounded dated availability reuse the Phase 9B
transaction wrapper and tenant scoping. No scheduling engine/domain replacement or migration
was introduced. The schema remains seven migrations / twenty-three mapped entity tables.

| Method | Route after B | Request/query | Success |
| --- | --- | --- | --- |
| GET | `/work-areas` | None | 200 `WorkAreaView[]`, including archival state |
| POST | `/work-areas` | `WorkAreaInput` | 201 `WorkAreaView` |
| PUT | `/work-areas/{workAreaId}` | `WorkAreaInput`, current version required | 200 `WorkAreaView` |
| POST | `/work-areas/{workAreaId}/archive` | `VersionInput` | 204 |
| GET | `/services/{serviceId}/scheduling-requirements` | Optional `category` | 200 `RequirementView[]` |
| PUT | `/services/{serviceId}/offerings/{category}/scheduling-requirement` | `RequirementInput` | 200 `RequirementView` for create/update |
| POST | `/services/{serviceId}/offerings/{category}/scheduling-requirement/disable` | `VersionInput` | 200 disabled `RequirementView` |
| GET | `/scheduling-employees` | None | 200 active `EmployeeView[]` |
| GET | `/employees/{membershipId}/availability` | Required `fromUtc`, `toUtcExclusive`; optional `page`, `pageSize` | 200 `AvailabilityView[]` |
| POST | `/employees/{membershipId}/availability` | `AvailabilityInput` | 201 `AvailabilityView` |
| PUT | `/employees/{membershipId}/availability/{availabilityId}` | `AvailabilityInput`, current version required | 200 `AvailabilityView` |
| POST | `/employees/{membershipId}/availability/{availabilityId}/archive` | `VersionInput` | 204 |
| POST | `/appointments` | `BookingInput` with required `clientRequestId` | 201 new / 200 replay, `AppointmentView` |
| GET | `/appointments/{appointmentId}` | None | 200 current `AppointmentView` |
| GET | `/appointments` | Required `fromDate`, `toDateExclusive`; optional `page`, `pageSize`, `status` | 200 `AppointmentView[]` |
| POST | `/appointments/availability-preview` | `AvailabilityCheckInput` | 200 `AvailabilityCheckView` |
| POST | `/appointments/{appointmentId}/reschedule` | `RescheduleInput` | 200 updated `AppointmentView` |
| POST | `/appointments/{appointmentId}/cancel` | `VersionInput` | 200 cancelled `AppointmentView` |
| GET | `/appointments/{appointmentId}/events` | None | 200 ordered `EventView[]` |

JSON write bodies and `Content-Type: application/json` are required. Empty/null/malformed
bodies, invalid UUIDs, wrong value types and missing required values produce 400. Input DTOs and
validation reuse Application contracts. `singleStageConfirmed` must be explicitly present,
including when false; it cannot silently default to false when updating a requirement.

Successful private responses use `Cache-Control: no-store`. The group retains the existing
`authentication` rate limiter (60 requests/minute per remote IP, 429 on exhaustion). These are
owner management endpoints; there is no public or customer scheduling route.

## Configuration contracts and examples

All UUIDs, versions, dates and pilot values below are illustrative; use actual IDs/versions from
current API responses. No prices, resource capacities or commercial policies are seeded.

Work-area creation and update (`version` is required only on update):

```json
{"name":"Yıkama alanı","capacity":2,"version":null}
```

Example `WorkAreaView` (GET returns an array of these):

```json
{"id":"11111111-1111-1111-1111-111111111111","businessId":"22222222-2222-2222-2222-222222222222","name":"Yıkama alanı","capacity":2,"archivedAt":null,"version":731}
```

Requirement PUT example:

```json
{"workAreaId":"11111111-1111-1111-1111-111111111111","singleStageConfirmed":true,"version":null}
```

Create requires an existing active service/category offering and active area. Update requires the
current requirement version. PUT returns 200 in both cases, so it does not fabricate a creation
status from a pretransaction read. Categories reuse the catalog vocabulary:
`Car`, `Suv`, `Van`, `Pickup`, `Motorcycle`, `Other`.

Example `RequirementView`:

```json
{"id":"33333333-3333-3333-3333-333333333333","businessId":"22222222-2222-2222-2222-222222222222","serviceId":"44444444-4444-4444-4444-444444444444","category":"Car","workAreaId":"11111111-1111-1111-1111-111111111111","singleStageConfirmed":true,"version":732}
```

Disable withdraws `singleStageConfirmed`, retains the requirement and its stable references,
and returns it with `singleStageConfirmed:false` and a new version. It is allowed only when the
Phase 9B ongoing/future appointment guard permits the change. This is a soft disable using the
existing supported state, not a new archival lifecycle or deletion. It remains possible after
service/area archival or offering removal. Reenable through PUT with `true`, a current version
and valid active configuration. Listing can show retained requirements after catalog changes;
an existing service with no matching requirement returns `[]`, and an out-of-tenant service 404.

Availability creation/update example (UTC instants; active intervals have minute precision):

```json
{"startsAtUtc":"2030-01-02T08:00:00Z","endsAtUtc":"2030-01-02T18:00:00Z","version":null}
```

Example `AvailabilityView`:

```json
{"id":"55555555-5555-5555-5555-555555555555","businessId":"22222222-2222-2222-2222-222222222222","membershipId":"66666666-6666-6666-6666-666666666666","startsAtUtc":"2030-01-02T08:00:00+00:00","endsAtUtc":"2030-01-02T18:00:00+00:00","archivedAt":null,"version":733}
```

List example:
`GET B/employees/{membershipId}/availability?fromUtc=2030-01-02T00:00:00Z&toUtcExclusive=2030-01-03T00:00:00Z&page=1&pageSize=20`.
Supply ISO 8601 instants with an explicit offset; nonzero offsets are normalized to UTC.
The query uses half-open overlap, a positive maximum 31-day elapsed interval, default page 1 /
size 20, page 1–10000 and size 1–50. It returns retained archived rows with `archivedAt` so callers
can display history deliberately. A foreign/missing membership returns 404. Creating/updating
requires an active Employee grant; availability reductions/archival cannot uncover existing
ongoing/future employee reservations.

Archive, cancel and disable bodies use the same positive `VersionInput`:

```json
{"version":733}
```

Work-area capacity cannot be reduced below actual ongoing/future peak reservations, and areas
with those reservations cannot be archived. Existing Phase 9B guards remain authoritative.

## Appointment creation and durable idempotency

Example `POST B/appointments`:

```json
{
  "clientRequestId":"77777777-7777-7777-7777-777777777777",
  "localCustomerId":"88888888-8888-8888-8888-888888888888",
  "localVehicleId":"99999999-9999-9999-9999-999999999999",
  "serviceId":"44444444-4444-4444-4444-444444444444",
  "expectedServiceVersion":725,
  "expectedVehicleVersion":726,
  "startLocal":"2030-01-02T10:00",
  "utcOffsetMinutes":180,
  "employeeMembershipIds":["66666666-6666-6666-6666-666666666666"],
  "source":"OwnerPhone"
}
```

The idempotency key is the existing **body `clientRequestId` UUID**, not a new header contract.
It is required, must be a valid JSON UUID and cannot be empty. UUID parsing bounds the format /
length rather than accepting an arbitrary free-text key. Clients persist and reuse it for retries.
There is no alternative automatic key or in-memory idempotency cache.

The Phase 9B key is business-scoped. The canonical hash includes resolved start, sorted employee
IDs, customer/vehicle/service IDs, expected catalog/vehicle versions and source. Same key/hash
returns **the exact original successful `AppointmentView` with 200**; first creation returns 201
and a `Location` header pointing to the detail route. A changed canonical payload returns 409
`idempotencyConflict`. Concurrent identical HTTP requests use independent contexts/connections,
produce one 201 and one 200, and persist one appointment/receipt. Failed transactions have no
success receipt. Retry comparison uses the original timezone snapshot before checking current
configuration; service/timezone changes or rescheduling cannot reinterpret an earlier request.

The API returns the existing `AppointmentView` directly; the internal `SchedulingResult.Created`
selects HTTP status and is not an extra response wrapper. Snapshot prices remain exact decimal
strings; statuses, sources and event kinds are named JSON enums. Example response structure
(abbreviated reservations/events arrays here; actual responses include the full arrays):

```json
{
  "id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "businessId":"22222222-2222-2222-2222-222222222222",
  "version":734,
  "source":"OwnerPhone",
  "status":"Confirmed",
  "localCustomerId":"88888888-8888-8888-8888-888888888888",
  "localVehicleId":"99999999-9999-9999-9999-999999999999",
  "serviceId":"44444444-4444-4444-4444-444444444444",
  "workAreaId":"11111111-1111-1111-1111-111111111111",
  "serviceName":"Dış yıkama",
  "customerName":"Örnek Müşteri",
  "vehiclePlate":"34 TEST 1",
  "category":"Car",
  "price":"450.10",
  "currency":"TRY",
  "activeMinutes":30,
  "waitingMinutes":60,
  "employees":1,
  "timeZoneId":"Europe/Istanbul",
  "startsAtUtc":"2030-01-02T07:00:00+00:00",
  "activeEndsAtUtc":"2030-01-02T07:30:00+00:00",
  "reservations":[],
  "events":[]
}
```

A replay intentionally retains its original version/status/history even after a later transition.
Use detail/calendar to refresh current state before further mutations. Creation requires current
service and vehicle versions. The service derives active duration/price/staff from the approved
catalog; the API offers no arbitrary duration or price input. Waiting does not consume resources.
Customer/vehicle ownership is checked by scoped IDs, not names/plates. Employees must be explicit,
distinct, active Employee grants and exactly the required count. `OwnerPhone` is the only accepted
source; no Customer/walk-in/automatic-assignment flow is introduced.

## Versioning, history, calendar and preview

Versions are positive uint32 PostgreSQL `xmin` tokens, opaque to clients. Read and echo them;
do not increment them or infer chronology. There is no new ETag/If-Match protocol. Stale
reschedule/cancel versions return 409 `scheduleConflict`, consistent with existing APIs.

Example reschedule:

```json
{"version":734,"startLocal":"2030-01-02T11:00","utcOffsetMinutes":180,"employeeMembershipIds":["66666666-6666-6666-6666-666666666666"]}
```

Rescheduling/cancellation return the updated `AppointmentView` with a new version. Reservations
are released/replaced atomically, agreed snapshots stay immutable and ordered events remain.
A cancelled appointment cannot be rescheduled or cancelled again; current-version attempts
return 409 `invalidStatus`. Cancellation remains possible when current catalog/grants are no
longer bookable. Full history is projected from authorized detail, not a separate controller query:

```json
[{"sequence":1,"kind":"Created","status":"Confirmed","previousStart":null,"previousEnd":null,"newStart":"2030-01-02T07:00:00+00:00","newEnd":"2030-01-02T07:30:00+00:00","occurredAt":"2026-10-27T12:00:00+00:00"}]
```

Calendar example:
`GET B/appointments?fromDate=2030-01-02&toDateExclusive=2030-01-03&page=1&pageSize=20&status=Confirmed`.
Dates are local dates in the **current business IANA timezone**, and form `[fromDate,toDateExclusive)`.
The application converts both local midnight boundaries to UTC and returns appointments overlapping
that interval. A 00:30 Istanbul job can start on the previous UTC date and still belongs to the
correct business-local calendar day. Ambiguous/invalid midnight boundaries fail safely rather
than using a machine timezone. Appointment `timeZoneId` is its original snapshot, which may differ
from current business settings after a timezone change.

Calendar is bounded to 1–31 local dates, page 1–10000, page size 1–50 (defaults 1 / 20), sorted by
UTC start then ID. Optional status is a defined `Confirmed`/`Cancelled` value. Responses are arrays,
not a new total-count/page envelope. Availability query bounds use UTC instants, as described above.

Preview example:

```json
{"serviceId":"44444444-4444-4444-4444-444444444444","category":"Car","startLocal":"2030-01-02T10:00","utcOffsetMinutes":180,"employeeMembershipIds":["66666666-6666-6666-6666-666666666666"]}
```

Example response:

```json
{"available":true,"startsAtUtc":"2030-01-02T07:00:00+00:00","activeEndsAtUtc":"2030-01-02T07:30:00+00:00"}
```

This is a **nonbinding observation**, not a hold/reservation or guarantee that create will succeed.
The engine checks current hours, configured area capacity and selected employee coverage/conflicts
again when creating. Ordinary capacity/coverage collisions return `available:false`; invalid or
unavailable configuration, grants, times or out-of-hours requests use the documented faults.
`startLocal` is exactly `yyyy-MM-ddTHH:mm` in the business zone. DST gaps are rejected; folds
require an explicit valid `utcOffsetMinutes`. No overnight active work or multi-stage inference.

## Error contract

Uses the existing safe Turkish ProblemDetails customization. Only explicit typed SchedulingFault
statuses/codes are mapped; arbitrary exceptions are not treated as user validation failures.
Exception messages, SQL, stack traces, paths, query strings, tenant IDs and diagnostic details
are never serialized. `detail` and `instance` stay absent. A `traceId` supports server-side diagnosis.
Fixed Turkish field-level errors are included on validation responses when available.

| HTTP | Meaning | Representative code |
| --- | --- | --- |
| 400 | Malformed/missing JSON, invalid UUID/version/range/page/category/source/employee selection/time | `validation`, `invalidCategory`, `invalidEmployees`, `invalidLocalTime`, `ambiguousLocalTime` |
| 401 | Missing/invalid JWT, expired/revoked session, invalid actor/session claims | `unauthenticated` at middleware; `unauthorized` for a transaction-time session failure |
| 403 | Missing/revoked membership or Owner grant, including Customer/Employee-only contexts | `forbidden` |
| 404 | Missing/out-of-tenant record, service/category requirement or local ownership relationship | `resourceNotFound` |
| 409 | Booking capacity/employee coverage conflict | `resourceConflict` |
| 409 | Changed catalog/vehicle/appointment/configuration version | `scheduleConflict` |
| 409 | Same business/request key, different canonical payload | `idempotencyConflict` |
| 409 | Current state/settings forbid operation | `invalidStatus`, `areaInUse`, `capacityInUse`, `availabilityInUse`, `requirementInUse`, `unsupportedStages`, `serviceUnavailable`, `areaUnavailable`, `employeeUnavailable`, `outsideHours`, `hoursUnconfigured`, `requirementChanged` |
| 500 | Unexpected internal failure, including unclassified persistence failure | `unexpected` |

Existing method/content-type/rate-limit behavior also remains 405/415/429 with safe ProblemDetails.
A user with no access to business B gets 403 whether or not a requested record exists. An authorized
Owner in B who supplies another tenant's record ID gets a scoped 404 without revealing its tenant.
Foreign/invalid employee selection is the existing generic `employeeUnavailable` 409; no foreign
employee name, ID or business is echoed in the error. Resource/state conflicts remain 409, whereas
malformed or intrinsically invalid inputs are 400; this API does not introduce 422 or 412.

Example:

```json
{"type":"https://httpstatuses.com/409","title":"İstek mevcut durumla çakışıyor.","status":409,"code":"scheduleConflict","traceId":"example-trace"}
```

## OpenAPI

The baseline did not contain an OpenAPI registration/package. Added the official
`Microsoft.AspNetCore.OpenApi` **10.0.13** package and authenticated `GET /openapi/v1.json`.
The v1 document deliberately includes the new `scheduling` group only; it is not a retrospective
specification of Phase 1–8 endpoints. It contains 19 operations, unique operation IDs, summaries,
request/response schemas, explicit mandatory write bodies, standard errors and the JWT Bearer
security scheme. Description text states that current business Owner authorization is required;
a bearer scheme alone does not imply an Owner grant. Document access needs a valid JWT but does
not expose tenant data or require selecting a business. No Swagger UI was added.

## Verification

All database-dependent verification used a new disposable PostgreSQL 17 container/database;
the existing developer database was never migrated or reset. Existing fixtures create uniquely
named schemas within that test database and apply only the existing migrations there. No new
migration, `dotnet ef database update`, `EnsureCreated` or `EnsureDeleted` was used.

Commands executed from the repository root:

```sh
docker run --detach --name washflow-phase9c-20261027-86d6129a \
  -e POSTGRES_USER=washflow_test -e POSTGRES_PASSWORD=phase9c_disposable \
  -e POSTGRES_DB=washflow_phase9c_disposable -p 127.0.0.1:55439:5432 postgres:17

dotnet restore backend/WashFlow.sln --disable-parallel -p:NuGetAudit=false
dotnet build backend/WashFlow.sln --no-restore -m:1 -nr:false

dotnet test backend/tests/WashFlow.UnitTests/WashFlow.UnitTests.csproj \
  --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal' \
  --logger 'trx;LogFileName=phase9c-unit.trx'

WASHFLOW_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=washflow_phase9c_disposable;Username=washflow_test;Password=phase9c_disposable' \
  dotnet test backend/tests/WashFlow.IntegrationTests/WashFlow.IntegrationTests.csproj \
  --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~SchedulingHttp \
  --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=phase9c-api.trx'

WASHFLOW_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=washflow_phase9c_disposable;Username=washflow_test;Password=phase9c_disposable' \
  dotnet test backend/tests/WashFlow.IntegrationTests/WashFlow.IntegrationTests.csproj \
  --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal' \
  --logger 'trx;LogFileName=phase9c-integration.trx'

git diff --check
```

Executed on 27 October 2026 against **PostgreSQL 17.11 (Debian 17.11-1.pgdg13+2)**:

| Final check | Passed | Failed | Skipped | Blocked |
| --- | ---: | ---: | ---: | ---: |
| Complete unit suite | 252 | 0 | 0 | 0 |
| Complete integration suite | 146 | 0 | 0 | 0 |
| Filtered scheduling HTTP/API suite | 18 | 0 | 0 | 0 |

The filtered 18 cases are a subset of the 146 integration cases, not extra tests: 17 new real
PostgreSQL HTTP tests and 1 new all-route unauthenticated test; the other 128 integration cases
are existing tests. Eight new contract unit cases extend the previous 244. Combined final total:
**398 passed, 0 failed, 0 skipped, 0 blocked**. Full solution build: **0 warnings, 0 errors**.
`git diff --check` passed. The current model/migration consistency checks also passed unchanged.
All counts were read back from the executed TRX files in the ignored `backend/tests/*/TestResults/`
folders (`phase9c-unit.trx`, `phase9c-api.trx`, `phase9c-integration.trx`).

The disposable container and its temporary volume were removed after verification:

```sh
docker rm --force --volumes washflow-phase9c-20261027-86d6129a
```

Final working tree: **5 added files, 7 modified files**, all unstaged/uncommitted; no deleted files.
Branch/HEAD remain `feature/phase9-scheduling` / `040769b`.

The new tests exercise all unauthenticated routes, valid Owner access, Customer/Employee denial,
revoked grants/membership/session, cross-business ownership and record access, malformed JSON,
required idempotency/version/confirmation fields, bounded queries, configuration CRUD/guards,
full booking lifecycle/history, exact replay after changes, current/stale versions, capacity and
employee collisions, local-date/UTC boundaries, preview, safe internal errors and OpenAPI metadata.
A dedicated PostgreSQL test revokes the Owner grant between boundary authorization and the
transaction to prove the second check. HTTP concurrency tests synchronize at the database lock
and assert independent PostgreSQL backend process IDs, not merely overlapping Task lifetimes.

Intermediate runs are not counted as final passes: the first API run had 1 pass / 17 failures in
common fixture setup because a captured email-verification timestamp preceded user creation.
The fixture now stamps verification after construction. The next run had 17 passes / 1 failure:
OpenAPI omitted the mandatory request-body flag because handler input is nullable to allow safe
Turkish validation. The document now explicitly marks those bodies required, matching actual
400 behavior, and the corrected filtered API suite passed 18/18. No authorization or database
constraint was weakened to fix either test failure. No SQLite/InMemory substitute was used.

## Changed files, limitations and Phase 9D readiness

Added: `SchedulingEndpoints.cs`, `SchedulingHttpTests.cs`, `SchedulingHttpPostgresTests.cs`,
`SchedulingApiContractTests.cs`, and this document. Modified: API Program/project reference and
exception handler, scheduling interface/contracts/validation, and `EfSchedulingService` for three
missing typed operations. No persistence mapping, migration, domain aggregate or Flutter file changed.

The Owner HTTP surface is ready for a separately authorized Phase 9D client/UI phase. A client
must obtain current business context, catalog/vehicle/configuration versions, persist request UUIDs,
show preview as advisory, and refresh detail after an idempotent replay or version conflict. The
business settings timezone governs calendar/local input; appointment timezone fields are snapshots.
No Flutter work was performed here and no production/developer database rollout was attempted.

Inherited Phase 9B limitations remain: business-wide serialization, full per-appointment retained
history, application-enforced scheduling rules on top of structural database constraints, no RLS,
and a requirement that all mutation writers follow the business-first locking protocol. An Owner's
legacy catalog/hours/vehicle changes can leave future appointments inconsistent with current
settings; this phase does not invent a global reconciliation/cancellation policy. Past-booking,
lead-time and commercial cancellation policies remain unspecified; the horizon constant is unused.
Requirement disable is reversible confirmation withdrawal, not a newly designed archival workflow.
Work-area/roster lists and single-appointment history retain existing service semantics; calendar
and dated availability are explicitly bounded/paged. No separate configuration detail endpoint or
new total-count envelope is claimed.

Customer self-service, Flutter, walk-in queues, multi-stage execution, overnight active work,
automatic assignment, notifications, offline synchronization, payments and work orders remain
outside this phase. GitHub issues remain open. Stop after Phase 9C.

Suggested commit only (not created):
`feat(scheduling): expose owner HTTP API and authorization (refs #4, #5, #11)`
