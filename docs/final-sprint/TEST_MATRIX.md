# Final sprint test matrix

25 November 2026. Tests below are source mappings, not proof of a new PostgreSQL run. Executed results recorded separately.

| ID | Scenario | Existing / planned automated evidence |
| --- | --- | --- |
| A | Open + staff available + area available | SchedulingPostgresTests.CreatePersistsSnapshotsReservationsEventsAndOriginalRetryAfterLifecycleAndCatalogChanges |
| B | Closed + staff available | Add explicit server regression |
| C | Open + staff unavailable | PreviewCalendarHoursAndAvailabilityAreAuthoritativeButPreviewDoesNotReserve; AvailabilityCoverageMergesAdjacentWindowsAndRejectsGapsAndStaleEdits |
| D | Exhausted area, available different employee | ConcurrentLastSlotHasOneWinnerWithIndependentConnections |
| E | Same employee overlaps with spare area capacity | EmployeeConflictIsRejectedWithSpareAreaCapacityIncludingConcurrentRequests |
| F | Different employees + enough area capacity | CapacityTwoAllowsTwoButRejectsThirdAndSameEmployeeCollision |
| G | Ends exactly at closing | SchedulingRulesTests.ActiveIntervalMustFitOneOpeningInterval; add server regression |
| H | Extends beyond closing | PreviewCalendarHoursAndAvailabilityAreAuthoritativeButPreviewDoesNotReserve |
| I | Shift ends before work completion | AvailabilityCoverageMergesAdjacentWindowsAndRejectsGapsAndStaleEdits; add explicit server boundary |
| J | Cancel releases resources | ReschedulingAndCancellationCheckVersionsAndReleaseResourcesAtomically |
| K | Concurrent last slot, at most one winner | ConcurrentLastSlotHasOneWinnerWithIndependentConnections |
| L | Conflicting reschedule leaves original | ReschedulingAndCancellationCheckVersionsAndReleaseResourcesAtomically; RescheduleAndCancelFailuresRollBackAlreadyFlushedReleasesAndKeepOriginalVersion |
| M | Hours shrink/closed/unset/zone edits preserve confirmed appointment | IncompatibleBusinessSettingsLeaveConfirmedBookingAndSettingsIntact (5 cases); HoursConflictHasDistinctSafeHttpCodeAndNeverChangesTheAppointment |
| N | Hours edit races with booking | ConcurrentHoursChangeAndBookingCannotBothInvalidateEachOther; independent-connection business-lock gate |
| O | Availability edits race with booking | ConcurrentShiftChangeAndBookingKeepConfirmedWorkCovered |
| P | Different areas permit concurrent work, same employee still blocked | MultipleRequiredEmployeesAllNeedFullCoverageAndDifferentAreasStillCheckEmployeeConflicts |
| Q | Business local date differs from UTC date | BusinessTimezoneDateBoundaryDeterminesClosedDayEvenWhenUtcDayIsOpen + existing Istanbul/DST tests |
| R | Multiple required employees | MultipleRequiredEmployeesAllNeedFullCoverageAndDifferentAreasStillCheckEmployeeConflicts + exact employee-count unit tests |
| S | Customer cancel/reschedule exactly 2h vs later | Pending customer booking implementation; original start cutoff, UTC instant comparison, owner policy separate |
| T | Countdown resumes / overdue / no auto-completion | Pending work-order implementation; server timestamps, injectable clock, widget tests |
| U | Finance, permissions, totals, no double counting | Pending financial module |

## Executed baseline

Backend `dotnet test backend/WashFlow.sln --no-restore -m:1 -nr:false`: 252 unit passed, 38 integration passed, 108 PostgreSQL skipped, zero failures (398 total). Flutter `flutter test --no-pub --reporter compact`: 341 passed. No PostgreSQL baseline proof yet. Test logs in /tmp are session evidence, not committed credentials.

## Final validation

P0 final run, 25 November 2026:

| Check | Exact result |
| --- | --- |
| `dotnet test backend/WashFlow.sln --no-restore -m:1 -nr:false --logger console;verbosity=minimal` with WASHFLOW_TEST_POSTGRES on dedicated PostgreSQL 17 at loopback 55439 | **415 passed, 0 failed, 0 skipped**: 252 unit + 163 integration. Integration duration 2m20s, unit 3s; compilation succeeded with TreatWarningsAsErrors enabled. |
| `flutter test --no-pub --reporter compact` | **343 passed**, 0 failures, 2m36s. |
| `flutter analyze --no-pub` | No issues found, 3.9s. |
| `git diff --check` | Passed. |

Backend has 17 additional test cases over the 398-test baseline; Flutter has two additional cases over 341. New database tests use only the disposable test database; the fixture creates/drops uniquely named test schemas. No migration was added or applied to the demo database. No live simulator walkthrough or hosted CI was run for this increment; no pushed commit exists at this point. Logs: /tmp/washflow-backend-final.log, /tmp/washflow-flutter-final.log, /tmp/washflow-flutter-analyze.log. Logs are local session evidence, not required source artifacts.


## Red/green evidence

- Isolated PostgreSQL baseline: 398/398 backend tests passed; 252 unit + 146 integration, zero skipped.
- Original code + five new incompatible-hours cases: 5 failed/0 passed, each showed unsafe SettingsStatus.Success. This is defect reproduction, not a final-suite failure.
- After fix, targeted scheduling/HTTP suite: 31 passed, zero failed/skipped (before three further clock/date/stale-editor regressions).
- An initial new Flutter adapter test exposed the shared ApiClient semantic-code allowlist; added only the trusted appointmentsOutsideHours code. Raw server error details remain discarded.

- An intermediate scheduling run exposed inconsistent test fixtures: weekly rows existed but Business.HoursConfigured was false. Both scheduling seed helpers now set the flag to true, matching real settings data. No production validation was weakened.


## Employee execution increment — final verification

25 November 2026: `dotnet test backend/WashFlow.sln --no-restore -m:1 -nr:false` with dedicated PostgreSQL 17: **422 passed, 0 failed/skipped** (254 unit + 168 integration). Integration 1m41s, unit 3s. `flutter test --no-pub --reporter compact`: **348 passed**, 0 failures, 1m57s. `flutter analyze --no-pub`: no issues (5.3s). `git diff --check`: passed. Migration `20261125082321_WorkOrderExecution` applied only inside dedicated disposable test schemas. No demo migration or live simulator rehearsal.

Intermediate test-only failures were corrected: test-clock initialization now captures one UTC instant before truncation; synthetic verified-user timestamps respect existing identity constraints; HTTP mock JSON uses UTF-8; overdue display rounds elapsed seconds down while positive remaining seconds round up. Existing validation was retained. Final suites contain all prior tests.


## Ordered resource-stage increment — final verification

25 November 2026: backend complete solution **429 passed, 0 failed/skipped** (254 unit + 175 integration, integration 1m50s, unit 4s) with disposable PostgreSQL 17. Flutter complete suite **351 passed**, zero failures (1m52s). Flutter analysis: no issues. Whitespace check passed. Commands use the same --no-restore/-m:1/-nr:false and --no-pub options recorded above. Logs: /tmp/washflow-staged-backend-final2.log, /tmp/washflow-staged-flutter-final2.log, /tmp/washflow-staged-analyze-final.log.

Seven additional PostgreSQL scenarios cover ordered resource scheduling and immutable cancelled-order planned-time history; three Flutter cases cover stage configuration and feedback. A regression specifically makes the first-sorted worker unavailable until stage two, proving selection follows actual availability rather than identifier order. The editor test revealed offscreen feedback on long forms; messages now scroll into view. No test or domain constraint was removed.

Idempotent upgrade scripts generated offline for review: /tmp/washflow-work-order-upgrade.sql (migration 7→8), /tmp/washflow-ordered-stage-upgrade.sql (8→9). Both migrations were exercised only in disposable test schemas; no demo migration or live simulator rehearsal.


## Customer self-service increment — 25 November 2026

- `dotnet test backend/WashFlow.sln --no-restore -m:1 -nr:false` with dedicated PostgreSQL port 55439: **436 passed** (254 unit + 182 integration), zero failures/skips. Log `/tmp/washflow-customer-backend4.log`.
- `flutter test --no-pub --reporter compact`: **358 passed**, zero failures/skips. Log `/tmp/washflow-customer-flutter-final.log`.
- `flutter analyze --no-pub`: no issues. Log `/tmp/washflow-customer-analyze-final.log`. `git diff --check` passed.
- Seven PostgreSQL customer regressions cover safe discovery/own vehicle/create/replay/current state, owner agenda source/nullable references, cross-customer privacy and revoked sessions, exact cutoff/late preservation, cross-source last-employee race despite spare area, failed-move/prepared-order rollback/history, configurable cutoff and fail-closed hours, populated schema-9 upgrade and guarded downgrade.
- Flutter exercises scoped adapters, malformed foreign payload rejection, explicit price confirmation, immutable retry key across uncertain outcomes, hidden mutations at cutoff and account-switch late-response disposal. Existing scheduling/work-order suites remain green.
- Offline migration10 SQL inspected: no deletion/reset/backfill of appointments. Disposable upgrade retains owner appointment and two active reservations. No demo migration applied.
