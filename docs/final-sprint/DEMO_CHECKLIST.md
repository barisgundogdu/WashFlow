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

| Step | Demonstration | Readiness |
| --- | --- | --- |
| 1 | Owner login, verification/session/context | Existing implementation; rehearse |
| 2 | Owner dashboard and navigation | Existing operational links; analytics pending |
| 3 | Service and category pricing | Existing; rehearse with configured service |
| 4 | Working area capacity | Existing; rehearse |
| 5 | Employee invitation and dated availability | Existing; execution pending |
| 6 | Customer discovers business and books own vehicle | Not implemented yet |
| 7 | Same-employee and full-area booking conflicts rejected | Existing engine; P0 regression tests passed; live demo rehearsal pending |
| 8 | Employee starts assigned work, denied access to other's work | Not implemented yet |
| 9 | Countdown survives navigation; overdue visible; no auto-completion | Not implemented yet |
| 10 | Complete stages and wash, owner sees progress | Not implemented yet |
| 11 | Record partial/full payment and expense | Not implemented yet |
| 12 | Financial dashboard separates earned/collected money | Not implemented yet |
| 13 | Customer own vehicle and appointment history | Vehicles existing; appointment history pending |
| 14 | Incompatible hours/shift edits rejected; explicitly resolve appointment | P0 fix and automated tests passed; live rehearsal pending |

## Evidence and limitations

Capture app/API/database evidence for each completed step and its issue/commit/CI run. Existing thesis screenshots are historical evidence, not proof of newly implemented flows. Record known limitations candidly. No mocked screen qualifies as completed backend functionality. No stage execution, customer bookings, finance or delivered operational notifications can currently be presented as working.
