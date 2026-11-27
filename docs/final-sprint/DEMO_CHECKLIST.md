# Final presentation checklist

25 November 2026. This is a rehearsal checklist, not a completed demo claim.

## Environment and safety

- Start existing PostgreSQL service; confirm the intended demo target before connecting. Preserve existing demo records and schema. Historical audited target is washflow_phase9e_db; reverify locally, do not silently use older washflow_db.
- Install/use .NET 10 SDK and Flutter SDK matching `.github/workflows/ci.yml` when possible. Start the iOS simulator with Xcode; select it with `flutter devices`.
- Supply ConnectionStrings__WashFlow and Jwt__Issuer, Jwt__Audience, Jwt__SigningKey through local environment/user-secrets; never commit or print credentials. Set ASPNETCORE_ENVIRONMENT=Development and ASPNETCORE_URLS=http://127.0.0.1:5080 for local simulation.
- Development memory email is local-only; production requires configured SMTP. Never claim mailbox extraction is email delivery.
- Backend: `dotnet run --project backend/src/WashFlow.Api`; check /health and /health/ready. No automatic migration or seed on startup.
- Mobile: `cd mobile`, `flutter pub get --enforce-lockfile`, `flutter run --dart-define=API_BASE_URL=http://127.0.0.1:5080` (API_BASE_URL verified in main.dart).
- Before approved schema changes: take a verified backup, review additive migration SQL, identify correct database, then apply with an authorized migration identity. Never reset/EnsureDeleted/reseed the demo database.
- Tests: use WASHFLOW_TEST_POSTGRES pointing only to a dedicated test database/PostgreSQL 17 service; fixture creates/drops unique test schemas. Never configure it against the demo database.
- Use existing authorized test identities or explicitly create synthetic demo accounts through registration/invitation and verification; no hardcoded credentials or invented production business list. Configure real business hours, staffing windows, area and service requirements through owner screens.

## Demonstration sequence and current readiness

| Step | Demonstration | Readiness / actual evidence |
| --- | --- | --- |
| 1 | Owner login, verification/session/context | Real auth/API setup and native context passed; credential forms retain existing tests |
| 2 | Owner dashboard and navigation | Native owner work-order/finance navigation passed; optional advanced analytics deferred |
| 3 | Service and category pricing | Real published-service API + customer UI rehearsed; owner editor covered by existing suites |
| 4 | Working area capacity | Two real configured areas used in native ordered execution; full-capacity races covered by PostgreSQL regressions |
| 5 | Employee invitation and dated availability | Real invitation acceptance and availability API used in native setup |
| 6 | Customer discovers business and books own vehicle | Real native UI/API passed; confirmation screenshot captured |
| 7 | Same-employee/full-area conflict rejection | Full PostgreSQL regressions passed, including owner/customer concurrency |
| 8 | Employee starts assigned work; other work is denied | Real native assigned-stage start/complete passed; cross-assignment API denial tests passed |
| 9 | Countdown survives navigation; overdue; no auto-completion | Real native reopen and 62-second overrun passed; foreground/resume also covered by widgets |
| 10 | Complete stages and wash; owner progress | Both real ordered stages and backend Completed statuses passed |
| 11 | Record partial/full payment and expense | Native partial payment/expense passed; full payment/reversal/correction in integration/widget suites |
| 12 | Financial dashboard separates earned/collected | Native report captured: earned450, collected150, expense100, cash50, unpaid300 TRY |
| 13 | Customer own vehicle and appointment history | Real native customer history showed completed work |
| 14 | Incompatible hours/shift changes require resolution | Real hours edit rejected without moving bookings; shift/configuration races covered by PostgreSQL tests |

## Evidence and limitations

[LIVE_REHEARSAL.md](LIVE_REHEARSAL.md) records the passing real iOS test, reviewed captures, setup and limits. [FINAL_STATUS.md](FINAL_STATUS.md) records readiness and remaining actions. Existing historical thesis screenshots remain intact; new screenshots contain synthetic rehearsal records only. The essential scenario is ready in the isolated environment. Deployment to the preserved demo still requires explicit migration approval and a verified backup. External SMTP/push delivery, physical-device/Android validation and optional advanced analytics are not claimed.
