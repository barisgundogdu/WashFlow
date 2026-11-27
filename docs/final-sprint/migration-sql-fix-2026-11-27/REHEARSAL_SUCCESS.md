# Executable migration SQL fix and restored-copy rehearsal

27 November 2026. Tracks [Issue #23](https://github.com/barisgundogdu/WashFlow/issues/23). Source baseline: `ed3f4f6eb5ed2108ae6ae4e63d25ca1866c76a3a`; fix branch: `codex/fix-idempotent-migration-sql`, targeting `feature/phase10-ui-ux`. No merge or demo deployment is included.

## Root cause and narrow source correction

`WorkOrderExecution.Up` and `OrderedStageScheduling.Up` supplied raw `CREATE TRIGGER` commands without a final semicolon. Direct EF migration execution accepts each top-level command without that delimiter. In an idempotent script Npgsql places the raw command inside `DO $EF$ BEGIN IF ... THEN ... END IF; END $EF$;`; a statement delimiter is then required before `END IF`. The original script failed at line141 accordingly. The defect is in the migration source's raw SQL, not psql transaction splitting, BOM handling, command transmission or the migration guard.

Added final semicolons to those two raw commands. Auditing every raw-SQL migration also found the same issue in two `OrderedStageScheduling.Down` commands (guard DO and DROP FUNCTION), which now terminate properly. Their existing data-loss refusal and all domain logic are unchanged. First seven migrations, schema snapshot/designer files, trigger bodies, constraints, role checks and tenant logic were not changed. No new migration ID, history rewrite, disabling operation or bypass was introduced. Already-applied schema11 databases have the same model/behavior; schema7 upgrades gain executable script output.

Audit:

| Migration | Raw SQL inspected | Result |
| --- | --- | --- |
| 2 EmailVerificationIssuanceIndexes | Historical duplicate-token UPDATE | Already terminated; unchanged |
| 3 EmployeeInvitationLifecycle | Historical invitation UPDATE/CTE | Already terminated; unchanged |
| 7 SchedulingPersistencePhase9B | Three functions, four triggers, Down cleanup | Already terminated; unchanged |
| 8 WorkOrderExecution | Work-order event trigger | Up terminator added |
| 9 OrderedStageScheduling | Identity function/trigger, stage trigger, Down guard/cleanup | Three final terminators added; guard semantics retained |
| 10 CustomerSelfServiceBooking | Down history/policy guard | Already terminated; unchanged |
| 11 OwnerFinancialLedger | Append-only/exact-reversal function and three triggers, Down guard/cleanup | Already terminated; unchanged |

## Executable regression and red/green evidence

New `IdempotentMigrationScriptPostgresTests` creates its own unique schema on the explicitly configured test service, directly migrates only to schema7, inserts a legacy business and generates the **complete schema7-to-latest idempotent script** through the production `IMigrator.GenerateScript` implementation. It executes the generated SQL through **psql `-X -qAt -v ON_ERROR_STOP=1` on stdin**, the same client/simple-query mode used in the failed rehearsal. It does not split or repair SQL and does not merely search for semicolons.

The test checks eleven migration IDs, legacy business data, all ten explicitly required trigger names, and complete constraint/index/trigger/function definitions against an independent direct-EF latest schema. Only schema-name differences and per-line edge whitespace introduced by the DO wrapper are normalized; SQL tokens and internal literals are retained. It then executes the same script again and verifies unchanged catalog/history/data. The fixture drops only its own unique synthetic schemas.

- Before source fix: the new test failed with the exact original `syntax error at or near END` diagnostic. Log `/tmp/washflow-migration-script-red.log`.
- During test development a catalog-query char cast and generated-function indentation comparison were corrected; these intermediate attempts were not recorded as passes.
- Final focused run: **1 passed**, log `/tmp/washflow-migration-script-green3.log`.
- Full backend suite: **446 passed = 192 PostgreSQL integration + 254 unit; 0 failed, 0 skipped**, log `/tmp/washflow-sql-fix-backend.log`.
- Flutter: **370 passed**, analysis **no issues**, logs `/tmp/washflow-sql-fix-flutter.log` and `/tmp/washflow-sql-fix-analyze.log`.

The workflow installs a PostgreSQL client for this regression before running the existing complete suite. Local macOS reproduction uses `WASHFLOW_TEST_PSQL_CONTAINER=washflow-final-sprint-tests` and the existing explicit `WASHFLOW_TEST_POSTGRES` test connection. In CI, psql connects directly to the disposable PostgreSQL service. Missing psql is a failure, not a silently skipped regression.

## Isolated target, backup and exact artifact

- Original **untouched demo**: `washflow_phase9e_db`, container `washflow-postgres`, loopback5432, PostgreSQL17.11. Read-only before/after snapshots confirm its seven migrations and every row/schema fingerprint unchanged.
- Approved restored target: **`washflow_migration_rehearsal_20261127_b9eff1a879`**, container **`washflow-final-sprint-tests`**, loopback **55439**. This is the retained copy restored from the verified archive during the failed rehearsal; rollback left it identical to schema7. No database was reset, dropped or overwritten for the retry.
- Prior validation restore `washflow_backupcheck_20261127_d333da8377` and all backups remain retained.
- Cluster system identifiers, container/name/port, `current_database()`, schema7 history and all 24 table fingerprints were positively checked before writes. Original and target use different PostgreSQL clusters. Identity and preceding-history guards ran inside each migration transaction.
- Original archive SHA256: `8bc01b38ce0b0be521b7cd45f76f3a48c4289e94e15338f9170d8d9007e472f7`. Archive not replaced/deleted; hash rechecked afterward.
- Corrected artifact: [upgrade-7-to-11-corrected.sql](upgrade-7-to-11-corrected.sql), SHA256 **`8bedb9f6acfcbb7cd929313b31bc50112de2db716968cd70906f09e18710f23e`**. Generated with `dotnet ef migrations script <schema7> <schema11> --idempotent --no-build`, with ambient application connection removed. The old failed script/hash remains untouched in the prior uncommitted inspection directory. Compared with the failed artifact, the migration transaction text differs only by the two necessary Up-statement semicolons. The published file additionally removes EF’s surplus empty EOF line. Its four transaction blocks are byte-identical to the successful first corrected rehearsal; the published file was also executed again against the upgraded copy through psql, preserving the complete catalog and all 34 table digests.

Each of the corrected artifact's four transaction blocks was executed through psql in order, with additional target/history guards and local lock/statement timeouts. Its migration statements were not patched during execution. No alternative EF update was used on this copy and EF history was not manually edited.

| Order | Exact applied migration | Public tables including EF history | Original baseline row projections |
| --- | --- | ---: | --- |
| 8 | 20261125082321_WorkOrderExecution | 28 | Unchanged |
| 9 | 20261125090139_OrderedStageScheduling | 30 | Unchanged |
| 10 | 20261125130449_CustomerSelfServiceBooking | 30 | Unchanged |
| 11 | 20261125132208_OwnerFinancialLedger | 34 | Unchanged |

All eleven expected IDs exist, with all seven original EF history rows preserved. Final catalog: **33 application tables + EF history, 342 columns including EF metadata (340 application columns), 272 constraints including 71 foreign keys, 139 indexes, 10 enabled user triggers, 5 functions**. Zero unvalidated constraints, invalid/not-ready indexes or disabled user triggers were found.

## Record counts and preservation

| Table | Schema7 before | Schema11 before smoke | After intentional smoke writes |
| --- | ---: | ---: | ---: |
| AppointmentEvents | 9 | 9 | 11 |
| AppointmentResourceReservations | 14 | 14 | 22 |
| AppointmentStages | 0 | 0 | 4 |
| Appointments | 6 | 6 | 8 |
| BusinessMembershipRoles | 5 | 5 | 10 |
| BusinessMemberships | 5 | 5 | 10 |
| BusinessServices | 3 | 3 | 4 |
| Businesses | 3 | 3 | 5 |
| CustomerProfiles | 2 | 2 | 5 |
| DeviceSessions | 14 | 14 | 19 |
| EmailVerificationTokens | 6 | 6 | 11 |
| EmployeeAvailabilities | 7 | 7 | 8 |
| EmployeeInvitations | 3 | 3 | 4 |
| Expenses | 0 | 0 | 1 |
| LocalCustomers | 3 | 3 | 3 |
| LocalVehicles | 3 | 3 | 3 |
| ManualIncomes | 0 | 0 | 0 |
| OwnedVehicles | 1 | 1 | 3 |
| PasswordResetTokens | 0 | 0 | 0 |
| PaymentMethods | 0 | 0 | 3 |
| Payments | 0 | 0 | 1 |
| RefreshTokens | 84 | 84 | 89 |
| SchedulingCreateReceipts | 6 | 6 | 8 |
| ServiceOfferings | 4 | 4 | 5 |
| ServiceSchedulingRequirements | 3 | 3 | 4 |
| ServiceStageTemplates | 0 | 0 | 2 |
| Users | 5 | 5 | 10 |
| WeeklyOpeningDays | 14 | 14 | 21 |
| WorkAreas | 4 | 4 | 6 |
| WorkOrderEvents | 0 | 0 | 5 |
| WorkOrderStages | 0 | 0 | 2 |
| WorkOrders | 0 | 0 | 1 |
| WorkStageAssignments | 0 | 0 | 2 |
| __EFMigrationsHistory | 7 | 11 | 11 |

Before smoke, every baseline table's original columns/count/full-row SHA256 matched exactly; only four new migration history rows and documented new default/null columns were added. Existing business, customer/vehicle, employee, service, appointment/reservation/event and authorization relationships were retained. There were initially **2 employee memberships and 3 owner memberships** in the five role rows.

After smoke, original primary-key sets (including composite role keys) were used to compare all old rows/columns against their baseline digests again: **all original 24 table projections match**, including the seven original EF history records. Increased totals above are intentional synthetic accounts/businesses/bookings/work orders/financial entries on the restored copy only; they are not production conversions or financial backfills. The original demo's full before/after data and schema digests also match exactly.

## Real upgraded-copy smoke evidence

The API was launched only after rechecking the isolated target's database/cluster identity and eleven migrations, using an explicit child-process connection to loopback55439 and test-only memory email/JWT configuration. Endpoint `http://127.0.0.1:5199/health/ready` was healthy. Existing demo configuration was not edited and no demo API was deployed/restarted.

1. Real registration, email verification and **owner login** succeeded. A fresh synthetic owner received explicit fixture memberships only on the copy, then read all **six restored original OwnerPhone appointments** through protected APIs. Original passwords/accounts were not reset; dates, local customer/vehicle links, reservations and events were checked. Private result `legacy-history-smoke.json`.
2. Real **iOS27 simulator / Flutter / Kestrel / upgraded restored PostgreSQL** test passed: **1 test, 2m25s** (Xcode build69.7s separately), log `/tmp/washflow-sql-fix-native.log`. The dedicated WashFlow Report Capture device was used; no normal demo token store/device was reset. Initial VM-log discovery stalled; the isolated test app was relaunched with identical arguments before any scenario assertion ran. The complete final scenario then passed, with no mocked HTTP or assertion weakening.
3. Actual customer business discovery → owned vehicle/service/slot → confirmation appeared in the owner's calendar with ordered-stage snapshots. Conflicting closed-hours changes were rejected without moving confirmed appointments.
4. Employee reopened the server countdown, ran past estimate for 62 real seconds and saw overdue time; owner saw the conflict warning. No auto-completion/shift occurred. Both ordered stages were explicitly completed and customer history showed completion.
5. Owner entered **150 TRY collection and 100 TRY expense** against a **450 TRY completed service** through Flutter. Reporting verified earned450, collected150, expense100, cash operating balance50, unpaid300. The later uncompleted appointment contributed no earned revenue.
6. Supplemental real HTTP smoke: a different new customer and owned vehicle tried the already-occupied employee slot while area capacity was available. Response **409 `resourceConflict`**; appointment/reservation counts stayed unchanged and the confirmed booking remained intact. Private result `capacity-conflict-smoke.json`.

Final read-only verification and private diagnostics are in `/private/tmp/washflow-sql-fix-rehearsal-20261127-4biqfzbs`; no personal records, passwords, tokens or database export are committed here. The synthetic test schemas used by the backend suite are in `washflow_final_test`, not the restored-copy public schema or original demo.

## Delivery and approval boundary

Source fix, executable regression, corrected artifact and this successful rehearsal are prepared for a focused PR linked to #23, targeting `feature/phase10-ui-ux`. Hosted CI status is recorded on the PR/checks after publication; local results above are actual completed runs. No automatic merge, history rewrite, branch deletion or existing-demo deployment is authorized.

The earlier failed report remains historical evidence; this report supersedes its stopped status only for the corrected isolated rehearsal. See [DEMO_UPGRADE_PROPOSAL.md](DEMO_UPGRADE_PROPOSAL.md) for the separately gated next stage. The original demo **still has schema7**, and requires a fresh durable encrypted backup, cutover preparation and another explicit approval before migration/deployment. The tested restored copy remains available; its extra smoke records must not be substituted for the demo database.
