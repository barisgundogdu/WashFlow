# Owner scheduling Flutter UI — Phase 9D recovery

Date: 2026-10-28. Requirements: SRS v0.2 FR-07/08/24 and NFR-07;
GitHub Issues [#4](https://github.com/barisgundogdu/WashFlow/issues/4) and
[#5](https://github.com/barisgundogdu/WashFlow/issues/5). Issue
[#11](https://github.com/barisgundogdu/WashFlow/issues/11) remains customer self-service
work outside this phase. Current issue descriptions, including completed foundation
Issue #9, were read during recovery. This document records implemented owner behavior,
not completion of those broader stories or Phase 9 as a whole.

## Git baseline and recovery audit

Required/actual branch: `feature/phase9-scheduling`. Required/actual HEAD:
`223df99bba59471bb1e9db106cbe10c1718804d0`. Initial working tree was clean.
Existing Phase 9D source was audited and extended in place. Phase 9A–9C, backend
endpoints, migrations and databases were not modified. No commit, push, issue closure,
file deletion or Kanban transition was performed. Changes remain local for review.

Before any edit, the requested commands were attempted. Sandbox restrictions prevented
Flutter writing its installed SDK cache; the checks were rerun with approved SDK access:

| Baseline check | Result |
| --- | --- |
| `flutter analyze` | PASS, 0 issues |
| `flutter test --reporter expanded` | FAIL: 303 passed, 3 failed, 0 skipped (306 tests) |
| `flutter build ios --simulator --debug` | PASS: `build/ios/iphonesimulator/Runner.app` |

The three failures were create/replay, calendar and roster API tests. Their mock
`http.Response` had no JSON/UTF-8 content type, so Turkish fixture text could not be
encoded correctly. This was a test-fixture failure, not evidence that the real API
routes failed. Several other API tests inspected requests but never asserted successful
response parsing; recovery added those assertions.

New widget coverage exposed a separate runtime defect: child scheduling routes sent
shared `ChangeNotifier` notifications from `initState` while another route was building.
Child loads now start after the first frame and verify mounted/current scope.

Other WIP gaps corrected:

- Booking option reload was unavailable after partial vehicle/requirement failures or
  stale catalog/vehicle versions. Reload now refreshes current selections and versions;
  stale create submissions remain disabled until that reload completes.
- Pending booking UUID/payload existed only in controller memory. The production owner
  route now supplies an owner-membership/business-scoped secure journal and supports
  exact replay after controller/application restart.
- Refreshing a removed service could dereference a missing configuration selection.
  Missing selections now clear safely.
- Availability failures could retain old rows or show a successful empty list. Rows
  clear before requests, and empty/loading/error/retry states are distinguished.
- Calendar/availability next-page controls could exceed page 10000. Calendar also
  checks the bound in the controller; service-level range checks remain authoritative.
- Business timezone was cached across calendar refreshes. Settings are now reread
  before calendar requests. Fractional-second availability input is rejected locally.

## Screens and navigation

The Turkish Owner home action **Planlama ve randevular** revalidates server contexts
before opening the tenant navigator route `/scheduling`. The route checks the current
Owner experience and all required service dependencies. Employee/Customer experiences
have no scheduling entry; unknown/root routes do not bypass the guard. Session generation,
identity, tenant and authorization checks are retained. The production route passes the
verified Owner membership ID for journal partitioning.

Implemented screens:

1. Calendar: day or seven-day range, date picker, previous/next period, explicit page
   navigation, refresh, loading, empty and retryable errors. Cards show current business
   local times, customer, plate, service and Confirmed/Cancelled status.
2. Appointment detail: exact price, agreed duration/waiting/staff snapshots, configured
   work area, assigned employee references, source, original timezone and retained event
   history. Archived/inactive references remain identifiable when names are unavailable.
3. Booking: tenant-local customer and vehicle, enabled service and matching vehicle
   category offering, required work area, explicit employee selection, local date/time,
   advisory availability preview, confirmation and exact retry.
4. Rescheduling: existing snapshot and required staff count, explicit active employees,
   business-local time/offset and confirmation. The current appointment version is sent.
   A failure locks the form until returning to/refetching detail; no automatic retry.
5. Work areas: list retained/archived state, create, edit name/capacity with current version,
   archive with confirmation and refresh after mutations.
6. Service requirements: actual catalog/category offering, choose an API-provided active
   area, explicit single-stage confirmation, save current requirement version, or disable
   retained confirmation, including requirements whose offering has since been removed.
7. Employee availability: explicit current employee, seven business-local dates converted
   to UTC bounds, paged retained windows, create/edit local interval, archive confirmation.

The API has no arbitrary work-area override on booking/rescheduling. Area selection is
implemented in service/category requirement configuration; the booking screen displays
that derived area. Offering category follows the selected vehicle. Staff count, durations,
prices and capacity come from current API data; employees are never auto-assigned.
Default editor times are editable suggestions, not business opening hours or policies.

## API integration verification

Contracts were compared directly against `scheduling-api-phase9c.md`,
`SchedulingEndpoints.cs` and `SchedulingContracts.cs`. `ApiSchedulingService` reuses
`ApiClient` and the current `AccessTokenProvider`. Each protected request sends the existing
Bearer JWT and scopes its path to `/api/v1/businesses/{businessId}`. Session revision
checks reject old-account token acquisition/HTTP results. Returned tenant/record identities
are checked before publishing. Backend Owner/membership and transaction-time authorization
are unchanged and remain the source of truth.

| Integration | Method/route after business base | Contract |
| --- | --- | --- |
| Calendar | GET `/appointments` | Local `fromDate`, `toDateExclusive`, page, size 50; direct array |
| Detail/history | GET `/appointments/{id}` | Current `AppointmentView`, including reservations/events |
| Create/replay | POST `/appointments` | UUID `clientRequestId`, customer/vehicle/service IDs and expected versions, local schedule, explicit employee IDs, `OwnerPhone`; 201 or 200 direct view |
| Advisory preview | POST `/appointments/availability-preview` | Service/category, local schedule and employees; `available`, UTC interval |
| Reschedule | POST `/appointments/{id}/reschedule` | Current `version`, local schedule and employee IDs |
| Cancel | POST `/appointments/{id}/cancel` | Current `version`; updated direct view |
| Work areas | GET/POST `/work-areas`; PUT `/{id}`; POST `/{id}/archive` | Name/capacity, nullable create version/current update version; archive 204 |
| Requirements | GET `/services/{id}/scheduling-requirements`; PUT `/services/{id}/offerings/{category}/scheduling-requirement`; POST same `/disable` | Actual area, explicit bool and version; disable uses version |
| Roster | GET `/scheduling-employees` | Active employee memberships |
| Availability | GET/POST `/employees/{membershipId}/availability`; PUT `/{id}`; POST `/{id}/archive` | UTC range/page for GET; UTC interval/current update version; archive 204 |

The separate GET events endpoint is supported by Phase 9C but is not needed: authorized
detail already includes full ordered history. No unsupported route, enum, response wrapper,
booking price/duration field or idempotency header is introduced. Catalog/customer/vehicle
and business settings continue through their established real API service interfaces.
Mocks/fakes exist only under `mobile/test`.

## State, money and timezone handling

The existing `SchedulingController`/`ChangeNotifier` coordinates calendar, request busy
state, safe errors, scope invalidation and pending booking state. Forms retain their local
selection state. Concurrent submissions are blocked. Async continuations check mounted and
current authorized scope before updating views; disposed controllers suppress notifications.
401/403 invalidate management access and delegate to existing session revalidation.

Decimal API price strings use the existing `Lira` integer-kuruş parser/display; no binary
floating-point conversion is introduced. Versions are opaque positive uint32 tokens,
echoed rather than incremented by the client.

`BusinessClock` uses the bundled IANA `timezone` data exclusively. Date/time inputs are
wall-clock containers, resolved to UTC using the current business zone. Appointment
`timeZoneId` remains a snapshot and is displayed separately. Calendar boundaries are local
dates; availability bounds are UTC instants. Day/week navigation never depends on the device
zone. DST gaps fail validation; repeated local times require an explicit valid UTC offset.
Ambiguous/invalid midnight boundaries fail safely. Calendar requests are one/seven dates,
with size 50 and pages 1–10000, and do not auto-fetch an unbounded calendar. Selection lists
follow existing customer/catalog pagination, with their existing 10000-page safety ceiling.

## Booking journal, retry and conflict semantics

Each logical create owns an immutable `BookingAttempt`. The fingerprint includes customer,
vehicle, service, expected service/vehicle versions, local time, UTC offset, sorted explicit
employee IDs and source. Identical retries reuse the exact UUID/body; corrected payloads
receive a new UUID after a definitive failure. An uncertain prior write prevents submitting
a changed payload until the owner resolves it.

Before POST, the production controller writes the exact request to `flutter_secure_storage`
using a versioned key scoped to business plus Owner membership. It contains IDs, versions and
schedule only, with no credentials or customer names/plates. iOS uses unlocked-this-device
Keychain accessibility, matching the authentication storage approach. There is one pending
create per owner membership/business on this device. A storage write failure prevents POST.
A storage cleanup failure after server success also preserves uncertainty for a safe replay.

A new scheduling controller reads that journal before enabling new booking. Recovered
requests can be retried without selecting current catalog/vehicle versions or active staff
again: the original body is submitted, allowing the backend to replay its original receipt
before current configuration checks. The owner explicitly confirms this retry. Successful
creation/replay clears the journal and opens a fresh detail GET because a replay returns the
original successful snapshot, which may now have an obsolete version/status/history.

Returning to the calendar to inspect an uncertain outcome preserves the journal and UUID.
Clearing an unresolved/unreadable journal requires explicit confirmation that the calendar
was checked; it never claims that a server appointment was cancelled. Unreadable storage
blocks new create while leaving the real calendar accessible. Storage retries and explicit
cleanup are available. Clearing storage is not automatic after uncertain failures.

409 reason codes map to fixed Turkish messages. Capacity/staff conflicts request reviewing
resources; `scheduleConflict`/`requirementChanged` require current data. Stale creation locks
until option refresh; stale rescheduling requires leaving/refetching detail. Cancellation
always attempts a current detail refresh after its result and never retries automatically.
Preview is advisory and grants no reservation/hold; the backend checks capacity again on save.

## Verification results

Final verification on 2026-10-28 (Flutter 3.47.7 stable, Dart 3.13.5,
Xcode 27.1 / 27A9275):

| Final check | Pass / fail / skip | Evidence |
| --- | --- | --- |
| `flutter analyze` | PASS, 0 issues | Exit 0, 1.2 seconds |
| Full `flutter test --machine` | 334 / 0 / 0 | Exit 0; machine reporter `done.success=true` |
| Scheduling subset within full suite | 56 / 0 / 0 | Domain 9, service 16, controller 11, store 1, widget 19 |
| `flutter build ios --simulator --debug` | 1 / 0 / 0 build | Exit 0; Xcode build 8.2 seconds, Runner.app produced |
| `git diff --check` | PASS | Exit 0, no output |

Machine totals exclude the 31 hidden test-loading events; none are skipped tests.
Baseline had 306 real tests; recovery added 28 tests and corrected the 3 fixture failures.
Intermediate test-authoring compilation/lint failures were corrected before this final run.
Final checks do not include a running-app smoke test or a live backend/database test.
Logs for this session are `/tmp/washflow-9d-baseline-{analyze,test,ios}.log`,
`/tmp/washflow-9d-final-analyze.log`, `/tmp/washflow-9d-final-test.jsonl`,
`/tmp/washflow-9d-final-ios.log` and `/tmp/washflow-9d-toolchain.log`.
These temporary local logs are not committed project artifacts.

Tests cover domain parsing/serialization, exact money, offsetless input rejection, Istanbul
previous-UTC-date conversion, London DST gap/fold, fractional-second validation, all used API
contracts and response types, both create success statuses, safe HTTP errors, tenant and
employee identity mismatches, token/HTTP results after identity change, calendar loading/error/
retry/range limits, creation/preview/employee selection, stale-version recovery, cancellation
confirmation, rescheduling versions, all configuration sections, removed service selection,
late disposed views, Owner-only navigation, secure-store scope and restart/replay semantics.

## Changed files and final working tree

Final branch/HEAD remain `feature/phase9-scheduling` / `223df99`. There are 11 modified
tracked files and 4 new untracked files, all belonging to this recovery; nothing is staged.

| File (repository relative) | Change |
| --- | --- |
| `mobile/lib/features/auth/presentation/session_screen.dart` | Supply the authorized Owner membership for secure recovery scope |
| `mobile/lib/features/scheduling/domain/scheduling.dart` | Journal interface/request restoration, storage errors, minute precision |
| `mobile/lib/features/scheduling/data/secure_booking_attempt_store.dart` (new) | Scoped secure pending-request storage |
| `mobile/lib/features/scheduling/presentation/scheduling_controller.dart` | Persistent retry lifecycle, storage failures, timezone refresh, page bound |
| `mobile/lib/features/scheduling/presentation/scheduling_screen.dart` | Production journal setup, recovery controls, bounded next page |
| `mobile/lib/features/scheduling/presentation/booking_screen.dart` | Safe route initialization, refreshable selections, stale recovery, original-request retry |
| `mobile/lib/features/scheduling/presentation/appointment_detail_screen.dart` | Safe route initialization, truthful cancellation/detail-refresh messages |
| `mobile/lib/features/scheduling/presentation/scheduling_configuration_screen.dart` | Safe route initialization, removed selections, availability loading/error states and bounds |
| `mobile/test/scheduling_domain_test.dart` | Journal round trip and fractional precision |
| `mobile/test/scheduling_service_test.dart` | Correct UTF-8 fixture, successful response assertions and session-identity races |
| `mobile/test/scheduling_controller_test.dart` | Restart recovery, storage failures, explicit cleanup and timezone/page guards |
| `mobile/test/support/scheduling_fakes.dart` | Capture requests/versions/ranges and configurable outcomes for tests |
| `mobile/test/scheduling_store_test.dart` (new) | Secure-storage replacement and tenant/membership isolation |
| `mobile/test/scheduling_widget_test.dart` (new) | 19 screen/navigation/error/retry/configuration regressions |
| `docs/features/scheduling-flutter-phase9d.md` (new) | Audit, contracts, verification and Phase 9E handoff |

The existing `app.dart`, `app_router.dart`, `api_client.dart`, Owner home, `main.dart`,
`pubspec.yaml`, `ApiSchedulingService` and scheduling widgets were inspected and retained;
their Phase 9D wiring/contracts already supported the required owner flow.

## Remaining limitations and Phase 9E checklist

Local unit/widget checks and a simulator build do not prove live Flutter–API–PostgreSQL
operation. No existing PostgreSQL database was changed or used for this recovery. Device
Keychain behavior was tested through the plugin's mock, not by terminating a running signed-in
app on a simulator/device. Phase 9E should perform that end-to-end evidence collection in an
explicitly designated isolated test environment.

- [ ] Run the app against Phase 9C using test Owner accounts in two separate businesses.
- [ ] Configure hours/timezone, catalog offering, area requirement, active employees and
      dated availability with actual backend IDs/versions.
- [ ] Create/preview/list/detail/reschedule/cancel through Flutter and retain sanitized evidence.
- [ ] Race two clients for the last employee/area capacity; verify one conflict and no double booking.
- [ ] Terminate after an uncertain POST, restart with the same membership, replay the original
      secure request, verify one backend appointment and refresh current detail.
- [ ] Verify journal isolation across owners/businesses, session revocation, Employee/Customer
      denial and backend rejection of foreign record IDs.
- [ ] Change catalog/vehicle/appointment versions from another client and verify Turkish stale
      recovery and correct next mutation version.
- [ ] Verify device timezone independence, business timezone changes, midnight overlap and DST
      on actual client/server timezone databases.
- [ ] Exercise real Keychain read/write/cleanup failures, compact devices, accessibility text,
      keyboard, date/time dialogs and offline-to-online manual retry.
- [ ] Record observed results in thesis evidence and update issue/board status only for work
      actually verified; broader #4/#5/#11 acceptance criteria remain open.

No customer self-service, walk-in queue, multi-stage execution, automatic assignment,
notifications, offline synchronization, overnight active work, payments or unapproved business
cancellation/lead-time policy was added. A secure retry journal is a single pending command,
not an offline booking queue. Missing historical employee names use retained IDs. Customer/
catalog selectors currently load their bounded pages into memory, so very large directories
may need a separately scoped searchable-picker improvement. Existing backend scheduling and
configuration reconciliation limitations described in Phase 9C still apply.

Phase 9D is ready for Phase 9E integration verification when the local checks below pass;
live acceptance and Phase 9 overall completion are not claimed. Stop after Phase 9D.

Suggested commit only (not created):
`feat(scheduling): recover owner Flutter scheduling UI and retries (refs #4, #5)`
