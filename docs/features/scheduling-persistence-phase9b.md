# Scheduling persistence and transactional engine — Phase 9B

Traceability: SRS v0.2 FR-07/08/24 and tenant/ownership requirements; Issues #4/#5/#11,
continuing the [Phase 9A foundation](scheduling-domain-phase9a.md). This is the owner-side
persistence/application foundation, not a usable HTTP/mobile scheduling feature or completion
of those issues. No GitHub issue or board state was changed.

Baseline: clean `feature/phase9-scheduling`, HEAD `4f9b9db`. No branch switch, commit, push,
existing-file deletion, Flutter change or HTTP endpoint was performed. HEAD remains unchanged;
the Phase 9B changes are intentionally uncommitted.

## Schema and migration

One additive migration: `20261027125056_SchedulingPersistencePhase9B` (generated with EF CLI
10.0.8 against EF runtime 10.0.13; the CLI emitted its older-tool warning). Its `Up` creates seven
tables, indexes, checks and history-protection triggers, and adds two alternate keys to existing
tables. It does not drop/recreate existing tables or transform/delete existing data. `Down` is the
normal explicit rollback of these new objects; it was not executed against any existing database.
Earlier migration files were not changed. The current model snapshot was updated.

| Table | Relationships and guarantees |
| --- | --- |
| `WorkAreas` | Business FK; `(BusinessId, Id)` alternate key; positive capacity, bounded nonblank name, one-way archival through Domain |
| `ServiceSchedulingRequirements` | Tenant/service/category unique; composite tenant FKs to stable service and work area; explicit single-stage confirmation |
| `EmployeeAvailabilities` | Composite tenant/membership FK; minute-precision positive UTC windows; retained archived windows |
| `Appointments` | Composite tenant FKs to service, area, customer and `(BusinessId, LocalCustomerId, LocalVehicleId)`; actor FK; business/request unique key; immutable snapshots |
| `AppointmentEvents` | Composite tenant/appointment FK; actor FK; unique tenant/appointment/sequence; append-only ordered history |
| `AppointmentResourceReservations` | Composite tenant FKs to appointment, area and employee membership; exactly one resource; positive UTC interval; one-way release retaining rows |
| `SchedulingCreateReceipts` | `(BusinessId, ClientRequestId)` primary key, unique tenant/appointment FK, immutable original successful response as `jsonb` |

The two additional existing-table keys are `(BusinessId, Id)` on `BusinessServices` and
`(BusinessId, LocalCustomerId, Id)` on `LocalVehicles`. Existing catalog/customer/vehicle/business
records are reused. Appointment identity references the stable service rather than a replaceable
offering row. Money snapshots use the catalog's `numeric(9,2)` precision and TRY currency.
Instants use `timestamp with time zone`; domain intervals normalize offsets to UTC. All six
scheduling domain entities use the existing shadow `Version`/PostgreSQL `xmin` convention.
The immutable technical receipt does not need an optimistic version.

Calendar indexes begin with tenant, then start/status. Partial indexes cover unreleased area and
employee overlap queries and active availability. Partial unique indexes prevent duplicate active
area/employee rows within an appointment. Event, interval, estimate, price, capacity, archival,
release and timestamp checks protect structural integrity. All FKs use `RESTRICT`.

PostgreSQL triggers prohibit event/receipt update or deletion, reservation deletion or rewriting/
reactivation, and changes to appointment identities/agreed snapshots. Only appointment status,
interval and update timestamp can change; cancellation cannot be reversed. Domain methods and
the service are still responsible for valid transitions and atomically writing their events.

## Typed application boundary and authorization

`ISchedulingService` provides typed work-area create/update/archive/list, service/category
requirement save/list, employee roster, availability save/archive/list, appointment create/detail/
reschedule/cancel, paged calendar and nonbinding availability preview. `EfSchedulingService`
implements it in Infrastructure using the existing application contracts and pure rules, and is
registered as a scoped service. No string/object dispatcher or HTTP API was introduced.

Every operation, including reads, checks the supplied actor against current persistent Owner
grants in the existing business membership/role model. A business ID is only a selector. The
actor's current user/session/security stamp, session ownership, expiry and revocation are checked
again inside the transaction. Both Owner and Employee grants may coexist, but an Employee-only
membership cannot use this boundary. Employee assignments require current active memberships
and explicit Employee grants; an Owner grant alone does not qualify an employee.

Every tenant-owned query includes the authorized business ID. Offering and grant queries follow
previously tenant-scoped service/membership lookups. Employee windows and reservations are
scoped by tenant as well as resource. Local customer/vehicle relationships are verified by ID,
never by matching name or license plate; platform-owned vehicles are not accepted as local ones.
The service rejects nesting into an existing transaction or a context with pending unrelated
changes, and clears its tracking cache before/after each unit of work to avoid stale tracked data
on reuse. Callers should use its registered scoped context as an independent unit of work.

## Locking and atomicity

Each operation begins a PostgreSQL **READ COMMITTED** transaction, then executes
`SELECT *, xmin FROM "Businesses" WHERE "Id" = ... FOR UPDATE` before any authoritative
configuration, reservation, idempotency or authorization read. The lock lasts through commit or
rollback. A waiting operation reads the winner's committed records after obtaining the lock.
There are no in-memory booking locks and no lock outside a transaction.

Lock order is business -> owner membership -> Owner grant -> user -> session -> selected
employee memberships/Employee grants in sorted ID order. User/session locks use `FOR SHARE`;
membership/grant locks use `FOR UPDATE`. This preserves the existing business-first protocol
used by settings, catalog, local customer/vehicle and invitation writes. Future membership and
configuration writers must keep that protocol. Reads currently take the business lock too;
this trades throughput for a simple consistent authorized view in the MVP.

Conflicts query committed, unreleased reservations in the same business. Work-area checks use
Phase 9A's clipped **actual peak concurrency**, with ends before starts at ties. Employee checks
merge adjacent availability windows and reject both coverage gaps and reservation collisions.
Intervals are half-open, so an appointment ending at 10:30 permits another starting at 10:30.

Create saves the aggregate, all reservations/events and original-response receipt in one
transaction. Reschedule loads complete history, checks status/version/current configuration,
excludes its own active reservations from conflict reads, flushes old releases, then appends new
reservations and an event in the same transaction. The release flush avoids transient conflicts
with active-resource unique indexes. A later failure rolls back even those flushed releases.
Cancel releases reservations and appends its event atomically. Parent updates are explicitly
marked so even an otherwise identical reschedule advances `xmin`; stale versions produce
`scheduleConflict` (409). Other failures roll back on transaction disposal; the context is cleared.

Scheduling entities keep the explicit operation timestamp shared by aggregate/events/releases
instead of replacing it with a second clock in `StampChanges`. Phase 1–8 stamping is unchanged.
The existing `TimeProvider` pattern is used for operation and session checks.

## Durable idempotency

The key is business-wide `(BusinessId, ClientRequestId)`, rather than actor-wide. The canonical
SHA-256 hash reuses Phase 9A's resolved UTC start, sorted explicit employee IDs, customer,
vehicle, service, expected service/vehicle versions and source. Different DST-fold occurrences
remain different requests. Actors must still pass current Owner/session authorization on retries.

After locking, create looks up the request before checking mutable catalog/ownership/versions.
On a hit, it hashes using the appointment's original timezone snapshot; later business timezone
changes cannot reinterpret an identical retry. Same key/hash returns the stored original response
with `Created=false`; a different hash returns `idempotencyConflict` (409). Initial success returns
`Created=true`. Separate contexts/services/connections see the same persisted result.

The extra receipt table is necessary to return the **original successful response**, including its
original version/history, after subsequent rescheduling/cancellation. Returning the current
appointment would silently change the successful create result. The receipt is written in the
same transaction and is immutable. Failed writes leave neither aggregate nor success receipt;
retry after a failure can succeed. Unique keys provide the durable uniqueness backstop; the
business lock serializes concurrent identical requests before they inspect those keys. No
process-local cache or transient success marker is used. Tests use new service/context/connection
instances for retries; they do not launch a separate OS process restart.

## Business rules and conservative configuration guards

- Creation copies authoritative catalog name, exact price/currency, active/waiting duration and
  required employee count, plus customer name, vehicle plate/category and business timezone.
  Expected service and vehicle versions must match. Client-supplied active duration is absent.
- One enabled, nonarchived service/category offering, one active area and an exact confirmed
  single-stage requirement are required. Publishing is not required for an owner-entered booking.
- Employee selection is explicit, distinct and has exactly the required count. No auto-assignment.
- The entire active interval must fit the current business-local opening interval. IANA zones,
  DST gaps/folds and UTC offsets use Phase 9A's strict conversion. Waiting consumes no resources.
- Reschedule preserves agreed price/duration/staff/snapshot values; current service/category,
  area and employee grants must still be available, and the local vehicle must remain active,
  owned by the same local customer and in the snapshotted category. A changed/removed offering
  does not rewrite history; an unavailable service/offering prevents rescheduling. Cancellation
  can release reservations even after catalog/employee configuration becomes unavailable.
- Area capacity cannot fall below existing ongoing/future peak occupancy. An area with ongoing/
  future reservations cannot be archived. Requirement resource/stage changes are rejected while
  affected confirmed appointments remain ongoing/future. Availability reduction/archival must
  preserve full coverage of existing ongoing/future employee reservations.
- Calendar dates are interpreted in the current business zone, use overlap queries and bounded
  1–31 date ranges, deterministic ordering and existing paging bounds. Ambiguous/invalid midnight
  boundaries fail safely. Preview is advisory and writes no reservation or idempotency record.
- No commercial lead-time, past-booking/horizon or cancellation deadline policy was invented;
  the Phase 9A horizon constant remains unused pending explicit policy decisions.

## Tenant and database limitations

Composite FKs independently reject cross-business area/service/customer/vehicle/employee/
appointment references, including a vehicle belonging to another local customer. A FK cannot
express active grants, revocation, availability coverage, business hours or peak capacity.
Those are rechecked in the locked application transaction. Raw SQL writers must not bypass
this protocol; the schema is not an alternative unrestricted booking API, and there is no RLS.
Reservation/appointment resource consistency, required resource counts and event completeness
are aggregate/application invariants in addition to structural database constraints.

Requirements deliberately have no FK to the replaceable `ServiceOfferings` row. Catalog Phase 8
replaces offering rows; a restrictive FK would break existing catalog updates/removal. A composite
FK to the stable tenant/service plus application category/offering checks preserves tenant safety
and history without coupling historical appointments to mutable offering identity. Actor FKs prove
user identity, while current membership/grants are authorization checks, not permanent historical
FKs to revocable grants. Employee conflicts remain business-local, as in Phase 9A.

Existing catalog/hours/vehicle/membership changes can make future appointments no longer match
current settings; this phase revalidates new/rescheduled bookings and guards the new scheduling
configuration operations, but does not introduce a global cancellation/reconciliation policy for
legacy management operations. Business-wide serialization and retained full aggregate history
are intentional MVP throughput/storage tradeoffs.

## Executed verification — 27 October 2026

Only a new disposable PostgreSQL container/database was used. No developer database was migrated,
reset or read by the tests. Existing `PostgresModelFixture` creates a uniquely named schema per
class inside that disposable database, applies migrations there, and drops that test schema on
fixture disposal. Real server version: **17.11 (Debian 17.11-1.pgdg13+2)**. No SQLite/InMemory proof.

Executed from the repository root:

```sh
docker run --detach --name washflow-phase9b-20261027-01a17d62 \
  -e POSTGRES_USER=washflow_test -e POSTGRES_PASSWORD=phase9b_disposable \
  -e POSTGRES_DB=washflow_phase9b_disposable -p 127.0.0.1:55439:5432 postgres:17

dotnet ef migrations add SchedulingPersistencePhase9B --no-build \
  --project backend/src/WashFlow.Infrastructure \
  --startup-project backend/src/WashFlow.Infrastructure --output-dir Persistence/Migrations

dotnet build backend/WashFlow.sln --no-restore -m:1 -nr:false

dotnet test backend/tests/WashFlow.UnitTests/WashFlow.UnitTests.csproj \
  --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal' \
  --logger 'trx;LogFileName=phase9b-unit.trx'

WASHFLOW_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=washflow_phase9b_disposable;Username=washflow_test;Password=phase9b_disposable' \
  dotnet test backend/tests/WashFlow.IntegrationTests/WashFlow.IntegrationTests.csproj \
  --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal' \
  --logger 'trx;LogFileName=phase9b-integration.trx'

git diff --check
```

The migration was inspected before running tests. Its application and repeat application/model
consistency were verified by the existing persistence suite (now 7 migrations / 23 entity tables).
No `dotnet ef database update`, `EnsureCreated` or `EnsureDeleted` command was used.

| Final verification | Passed | Failed | Skipped | Blocked |
| --- | ---: | ---: | ---: | ---: |
| Complete backend unit suite | 244 | 0 | 0 | 0 |
| Complete integration suite | 128 | 0 | 0 | 0 |
| Of integration: new scheduling PostgreSQL tests | 17 | 0 | 0 | 0 |
| Of integration: existing tests | 111 | 0 | 0 | 0 |

The two integration subset rows are included in 128, not additional tests. Total final tests:
**372 passed, 0 failed, 0 skipped, 0 blocked**. Solution build: 0 warnings/errors. Diff check passed.
Eight new model unit cases extend the previous 236 unit tests. New integration tests cover
creation/snapshots, lifecycle/history, stale versions, configuration guards, availability merging,
preview/calendar, invalid tenant/ownership FKs, session/grant checks, DST, idempotency, rollback,
capacity 1/2, actual peak/adjacency and employee collisions.

Concurrency tests synchronize independent contexts at their first business-lock command and
assert distinct PostgreSQL backend process IDs before releasing the barrier. They prove competing
last-slot allocation, capacity 2, same-employee collision with spare area capacity, and concurrent
identical-request deduplication. Failure injection after the aggregate write and after reschedule
release flushing verifies actual database rollback and retry safety. Direct SQL tests exercise
immutable-history/snapshot/check constraints separately from domain validation.

Earlier verification attempts: sandbox socket restrictions initially prevented the test runner
from starting; reruns with local socket access completed. The first filtered scheduling run had
15 passed / 2 failed / 0 skipped because rollback tests expected the injected exception directly
instead of EF's `DbUpdateException` wrapper. Expectations now assert both wrapper and inner fault;
the final full suite passes both rollback tests. The initial EF command's restore/build stalled and
was interrupted; the migration was generated with `--no-build` after a successful local build.
Earlier compile errors were corrected before the final successful build. These intermediate
failures are not counted as final passing results.

TRX evidence is in the ignored `backend/tests/*/TestResults/phase9b-*.trx` paths. Test infrastructure
and result counts were updated without weakening existing tests. The disposable container was
removed after verification; no persistent test service is required by the application.

## Changed files and Phase 9C handoff

Added:

- `backend/src/WashFlow.Application/Scheduling/ISchedulingService.cs`
- `backend/src/WashFlow.Infrastructure/Scheduling/EfSchedulingService.cs`
- `backend/src/WashFlow.Infrastructure/Scheduling/SchedulingCreateReceipt.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Configurations/SchedulingConfiguration.cs`
- The new migration and its generated `.Designer.cs` file
- `backend/tests/WashFlow.UnitTests/SchedulingPersistenceModelTests.cs`
- `backend/tests/WashFlow.IntegrationTests/SchedulingPostgresTests.cs`
- This document

Modified: Infrastructure DI, `WashFlowDbContext`, current EF model snapshot,
`IdentityModelTests`, `InfrastructureTests`, and `PersistenceModelTests` (updated expected
migration/entity counts and table probes). Existing migrations and Phase 9A domain logic remain
unchanged.

Phase 9C can add owner-only HTTP endpoints backed by `ISchedulingService`, deriving actor/session
only from validated server authentication claims, verifying Owner context again at the API
boundary, mapping typed scheduling faults safely to Turkish problem details, and specifying
request/version/idempotency replay semantics for clients. An idempotent create replay intentionally
returns its original version; use detail/calendar for current state before later mutations.
Configuration listing/paging/UI needs can be assessed there. Production migration rollout remains
an explicit future operation; the developer's existing database has not received Phase 9B.

Customer self-service, Flutter, walk-in queues, automatic assignment, ordered multi-stage work,
overnight active jobs, notifications, payments, work orders and global employee calendars remain
deferred. No endpoint or customer-booking readiness is claimed. Stop after Phase 9B.

Suggested commit only (not created):
`feat(scheduling): persist transactional booking engine (refs #4, #5, #11)`
