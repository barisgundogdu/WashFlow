# Scheduling domain foundation — Phase 9A

Traceability: Issues #4/#5/#11; SRS FR-07/08/24 and tenant/ownership requirements.
Baseline: clean `feature/phase9-scheduling` at `462c1f2dc6055c31fda3fdbd3e9c51a545e35ee8`,
the seven-file checkpoint on top of `de90a27`. This completes the domain/application foundation
only. It does not implement appointments as a usable application feature or complete any issue.

## Checkpoint review and changes

All seven checkpoint files were read before editing. Their six entities were retained:

| File | Checkpoint gap | Foundation behavior |
| --- | --- | --- |
| `Appointment.cs` | Freely mutable state/times; arbitrary status/source | Guarded create/reschedule/cancel; immutable agreed values; typed status/source; copied employee selection |
| `AppointmentEvent.cs` | Unchecked sequence/kind/status | Aggregate-produced, ordered events with old/new interval and actor |
| `AppointmentResourceReservation.cs` | Neither/both resources and reversible release representable | Exactly one nonempty resource; UTC positive minute interval; one-way release retaining history |
| `EmployeeAvailability.cs` | Empty/reversed windows and arbitrary edits | Validated dated UTC windows and guarded archival/update |
| `ServiceSchedulingRequirement.cs` | Unchecked identities and implicit scheduling support | Stable tenant/service/category/area identity and explicit single-stage confirmation |
| `WorkArea.cs` | Unbounded/blank text and nonpositive capacity possible | NFC bounded name, positive capacity, immutable tenant identity and archival |
| `SchedulingContracts.cs` | Rules mixed with contracts; string/object operation dispatcher; no typed views | Typed commands/views; separate pure rules/validation; dispatcher removed until a typed transactional store is designed |

New `SchedulingValues.cs` contains interval/snapshot values and lifecycle definitions.
`SchedulingRules.cs` and `SchedulingValidation.cs` separate calculation from request validation.
No existing Phase 1–8 implementation was modified.

## Domain invariants

- Tenant-owned entities require nonempty identities. Database referential constraints are deferred.
- Statuses are `Confirmed` and `Cancelled`; event kinds are `Created`, `Rescheduled`, `Cancelled`.
- `OwnerPhone` is the only specifically named approved source currently accepted. No additional
  Owner source, Customer source or walk-in behavior is inferred.
- A booking has one service, one local vehicle, one area and a distinct explicit employee set.
- Snapshot values copy service name, customer name, vehicle plate, category, price/currency,
  active minutes, nullable waiting minutes, employee count and timezone. They cannot be changed
  through rescheduling/cancellation. Price bounds follow the Phase 8 `numeric(9,2)` policy.
- Structural snapshot checks live in Domain. Application `CreateSnapshot` reuses the current
  catalog and vehicle-category vocabulary, checks matching tenant/customer/service identities,
  rejects unavailable services/archived vehicles and resolves the business timezone. Future
  persistence must construct snapshots through this authoritative application validation path.
- Names are checked for existing unsupported surrogate/control characters before trim/NFC,
  then checked against normalized storage lengths. No pilot values are seeded.
- The aggregate derives its end from active minutes and records one area reservation plus the
  required employee reservations. Waiting never extends constrained occupancy in this MVP.
- Cancellation releases active reservations and adds an event. Rescheduling releases old rows,
  adds replacements, preserves snapshots and records both intervals. Invalid domain inputs are
  validated before changing aggregate state.
- Event/reservation collection views are read-only. Released rows remain present and cannot be
  reactivated. Mutation times are explicit UTC instants and cannot move backwards.

`UpdatedAt` retains the existing `IEntity` public setter convention; protected business properties
are mutated through domain methods. No domain event is dispatched externally.

## Pure scheduling rules

`Occupancy` normalizes explicit offsets to UTC, requires minute precision and `start < end`.
Calculations validate even `default(Occupancy)` so struct defaults cannot bypass invariants.

Overlap uses half-open intervals `[start,end)`. `Peak` clips existing intervals to the candidate,
adds the candidate and processes ends before starts at ties. `HasCapacity` checks that actual
peak against positive capacity, rather than counting all overlapping rows. Empty reservation
sets are valid; empty availability provides no coverage.

`Covered` merges overlapping/adjacent windows and rejects gaps. `EmployeeAvailable` combines
complete coverage with absence of overlapping reservations. Callers must supply the exact
tenant/resource's active, unreleased records; these pure methods do not query or authorize them.

Hours validation reuses the complete Phase 6 seven-day contract. Active work must fit one
opening interval. Closing 1440 means the next midnight boundary. No overnight continuation,
splitting or name-based stage inference exists. Scheduling requires explicit single-stage
confirmation and an exact tenant/service/category/area match.

Time interpretation uses a supplied `TimeZoneInfo`, never the machine's local zone. IANA zone
lookup fails safely instead of falling back. Local input is exactly `yyyy-MM-ddTHH:mm`. DST gaps
are rejected; folds require an explicit valid offset. Ambiguous/invalid opening or closing
boundaries conservatively make that interval unavailable. Date overflow fails safely.

The inherited 180-day horizon constant is retained for the future transactional application;
it is not a clock-based booking guarantee in this phase. No commercial lead-time, cancellation
or lateness policy has been introduced.

## Contracts and idempotency foundation

Typed request/response contracts cover work areas, availability, requirements, roster, booking,
preview, calendar, detail, rescheduling and versioned commands. Calendar inputs are bounded to
1–31 dates, page 1–10000 and page size 1–50. They do not execute calendar queries.

Appointment responses use exact decimal-string prices and named JSON statuses/sources/events.
Response history is copied so later domain mutations cannot rewrite an earlier response.
Internal request hashes are not exposed in the appointment view.

Positive uint32 versions are required by application validation and comparison helpers. They
remain opaque persistence versions consistent with Phase 1–8 xmin conventions: no fake domain
version counter or database concurrency guarantee is provided.

Create identity is the existing `(actor, business, clientRequestId)` concept. The SHA-256 payload
digest uses resolved UTC start, sorted employee IDs, customer/vehicle/service IDs, expected
versions and source. It is independent of current culture and employee selection order.
Different occurrences of a DST fold produce different hashes.

**Phase 9B must look up an existing request ID before revalidating current configuration.**
For an existing request, resolve its local input using the original booking timezone snapshot
before comparing hashes. Later timezone/catalog changes must not turn an identical retry into
a new booking. Unique keys, transactional lookup and duplicate suppression are not implemented
or proven by this pure hash helper.

## Executed verification

Executed on 27 October 2026, without accessing a database:

| Command | Result |
| --- | --- |
| Checkpoint `dotnet test backend/tests/WashFlow.UnitTests/WashFlow.UnitTests.csproj --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'` | 139 passed, 0 failed/skipped |
| Final `dotnet build backend/WashFlow.sln --no-restore -m:1 -nr:false` | Passed; 0 warnings/errors |
| Final `dotnet test backend/tests/WashFlow.UnitTests/WashFlow.UnitTests.csproj --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'` | 236 passed, 0 failed/skipped; 97 new Phase 9A cases |
| `git diff --check` | Passed |

New tests are `SchedulingRulesTests`, `SchedulingDomainTests`, and `SchedulingContractsTests`.
They cover interval validation, adjacency, peak capacity 1/2, clipping/input order, availability
gaps, employee collisions/counts, hours/24:00/overnight limits, DST gaps/folds, UTC conversion,
snapshot bounds/ownership/stability, lifecycle/history, reservation XOR/release, source/identity
validation, JSON prices/statuses, canonical hashes and bounded contracts.

Existing unit tests also verified the unchanged six-migration/sixteen-entity EF model offline.
The solution build compiles integration projects but does not execute their tests. PostgreSQL
integration, migrations, APIs and Flutter were intentionally not run or implemented in Phase 9A.

## Phase 9B handoff and limitations

Ready to begin persistence work in a separately authorized phase:

1. Map the six entities, immutable snapshot fields and enum conversions; retain restricted,
   tenant-safe composite relationships and independent stable service identity.
2. Map aggregate backing collections. Lifecycle event sequencing assumes complete aggregate
   history; do not hydrate only a paginated subset before invoking transitions.
3. Use actual generated xmin values in view/command boundaries and force parent version advances.
4. Add unique create-request and active-resource keys, event ordering and interval constraints.
5. Implement business-level transactional locking, fresh membership/session/Employee-grant checks,
   authoritative record loading, configuration-change guards and active-reservation filtering.
6. Perform capacity checks before invoking aggregate mutations; transactionally persist the
   appointment, all reservation releases/replacements and events together.
7. Prove rollback, concurrent last-slot behavior and multi-instance safety on a new disposable
   PostgreSQL 17 instance. No such database guarantees are claimed here.

Application faults have safe Turkish messages/codes; domain invariant violations are Turkish
argument errors. Future boundaries must translate these deliberately rather than expose raw
database exceptions. Future rescheduling must additionally enforce current resource/service
availability and the approved conservative policy for disabled/archived services.

Employee conflicts remain scoped to a business. Authorization, revocation warnings, persistent
idempotency, safe configuration updates and historical database recovery are not implemented.
Customer self-service, combined stages, overnight execution, walk-in queues, automatic assignment,
work orders and payments remain deferred. Phase 9 as a whole is not complete.

No migration, EF mapping, database, endpoint, Flutter, issue-state, commit or push operation was
performed. Changes remain uncommitted for review. Stop after Phase 9A.
