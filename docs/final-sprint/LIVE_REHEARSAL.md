# Integrated native rehearsal

25 November 2026. Real iOS simulator + Flutter adapters/screens + Kestrel + PostgreSQL. **One complete end-to-end test passed**, with no skipped cases, in 2m25s (Xcode build 17.3s separately). Log: `/tmp/washflow-live-run3.log`. Test source: `mobile/integration_test/final_sprint_live_test.dart`.

## Isolation and actual coverage

A newly created `washflow_rehearsal_*` database on the separate PostgreSQL 17 test container at port 55439 was migrated through migration 11. Its readiness endpoint returned healthy on `http://127.0.0.1:5199`. No connection or migration targeted the preserved demo database or its container. The dedicated **WashFlow Report Capture** iOS 27.0 simulator was used; the normal demo device was not replaced/reset.

The harness creates unique owner/customer/employee accounts through real registration, local verification and invitation acceptance APIs. Passwords are generated in memory per run and never logged/committed. Auth tokens use a test-only memory store, without reading/clearing the normal app token-store key. Actual pending-booking/finance storage uses unique real profile/business/membership keys. No HTTP response is mocked, no production business list is hardcoded, and no database clock is advanced.

Verified sequence:

1. Owner configures timezone, hours, employee window, two work areas and a published service with two ordered one-minute stages; customer registers an owned vehicle.
2. Customer uses actual Flutter directory → business → own vehicle → price/duration → available slot → explicit confirmation. The owner reads the **same** Customer-source appointment, with no tenant-local identity link and both stage snapshots.
3. A later confirmed booking is created. A closed-hours edit is rejected; both original appointment times remain intact.
4. Owner creates/monitors the work order. Employee waits for the real planned start, starts assigned work through UI, leaves/reopens detail and gets the server-anchored countdown.
5. The test waits 62 real seconds: overdue appears, automatic completion does not occur, owner sees the potential-conflict warning, and the later booking remains unchanged.
6. Employee explicitly completes both ordered stages. Both stage statuses and the work-order status are verified Completed from the backend.
7. Owner enters 150.00 TRY partial payment and 100.00 TRY expense through Flutter for a 450.00 TRY completed service. Report API verifies earned revenue **450.00**, collections **150.00**, expenses **100.00**, cash operating balance **50.00**, unpaid balance **300.00**. The uncompleted later appointment contributes no earned revenue.
8. Customer's actual history UI shows the completed job. Final check finds no Flutter exception; synthetic sessions are logged out.

Two earlier exploratory harness runs stopped at an incorrect single-message expectation after both stages completed. The final assertion correctly requires two stage completion messages and independently checks both backend stage statuses. The final full run passed; no product validation was weakened to obtain it.

## Reviewed presentation captures

- [Customer confirmation](screenshots/customer-confirmed.png)
- [Employee overdue](screenshots/employee-overdue.png)
- [Owner warning and explicit reassignment guidance](screenshots/owner-warning.png)
- [Financial report](screenshots/finance-report.png)

All four captures were visually reviewed. They contain only synthetic rehearsal data and show the actual native interface. The simulator status bar is standardized; appointment/work timing comes from the server, not the status-bar clock.

## Reproduction

Use a **new explicitly verified disposable database**, PostgreSQL 17 and .NET 10. Keep the existing demo connection untouched. Supply the connection and a newly generated Base64 JWT key through local environment only; set `Jwt__Issuer`, `Jwt__Audience`, `ASPNETCORE_ENVIRONMENT=Development`, `Email__Provider=Memory`, and `ASPNETCORE_URLS=http://127.0.0.1:5199`. Review and apply migrations only to that new disposable target. Start the built API with `dotnet run --no-launch-profile --project backend/src/WashFlow.Api`, check `/health/ready`, then run:

```sh
cd mobile
flutter test integration_test/final_sprint_live_test.dart -d <isolated-ios-simulator-id> \
  --no-pub --dart-define=FINAL_SPRINT_API_URL=http://127.0.0.1:5199 --reporter expanded
```

The URL guard alone cannot prove database isolation; verify the server target before running. The harness retains generated business records in the disposable database rather than resetting it, and creates unique accounts each run. It assumes the local business date has at least ten minutes left before midnight, so run the rehearsal earlier in the day. Development mailbox extraction verifies local dispatch only; it does not claim SMTP/push delivery. The native test is opt-in; Linux CI runs backend/PostgreSQL, Flutter unit/widget tests and analysis, without pretending to run iOS hardware tests.
