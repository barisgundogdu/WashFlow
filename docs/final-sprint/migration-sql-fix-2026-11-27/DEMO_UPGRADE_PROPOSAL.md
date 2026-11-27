# Proposed original-demo upgrade — approval required

27 November 2026. **Not executed.** The successful restored-copy rehearsal is documented in [REHEARSAL_SUCCESS.md](REHEARSAL_SUCCESS.md). It does not authorize changes to `washflow_phase9e_db` or deployment.

## Release and migration gates

1. Review the focused fix PR for Issue #23 and its complete backend/Flutter CI. Obtain explicit approval before merging into `feature/phase10-ui-ux`; preserve history. Freeze the resulting release commit and matching Flutter/API artifacts.
2. Freeze the reviewed corrected 7→11 SQL artifact, SHA256 `8bedb9f6acfcbb7cd929313b31bc50112de2db716968cd70906f09e18710f23e`. Its four transactions apply `20261125082321_WorkOrderExecution`, `20261125090139_OrderedStageScheduling`, `20261125130449_CustomerSelfServiceBooking`, then `20261125132208_OwnerFinancialLedger`. If code/migration/artifact changes, re-review and repeat the isolated rehearsal; do not substitute an unverified script.
3. Review the preserved original database's current identity, seven actual migration IDs, owners and grants through read-only checks. The portable rehearsal restore uses the isolated `washflow_test` role with `--no-owner --no-acl`; it is not proof that original application-role privileges should be replaced. Confirm authorized migration/application roles and post-upgrade access before cutover.
4. Choose an approved durable encrypted backup location and retention/access policy. Retain all existing archives. Today's temporary verified archive is additional evidence, not a fresh cutover backup. Preserve old binaries and runtime secrets/configuration privately, never in Git.
5. Obtain **separate explicit approval** naming the exact original target `washflow_phase9e_db` / `washflow-postgres` / loopback5432, release commit, corrected SQL hash, maintenance window, writers to stop, coordinated Flutter/API rollout and recovery procedure. No current approval covers these actions.

## Proposed controlled cutover after approval

1. Stop every writer to the approved demo, verify no old API/background process can accept changes, and capture the final consistent baseline. Take a new full backup, verify SHA256 after copying to durable encrypted storage, restore it into a new isolated verification database and verify all tables/schema/relationships. Never overwrite an old backup or the original during verification.
2. Assert the original database/container/cluster identity and seven expected history rows immediately before any migration. Compare the reviewed artifact hash. Use explicit connection parameters; never rely on ambient defaults or point the application at the rehearsal copy.
3. Apply only the reviewed transaction blocks in order with `ON_ERROR_STOP`, identity/history guards and reviewed lock/statement timeouts. Verify each committed migration and all original row projections before proceeding. No manual EF-history edits, disabled triggers or constraint bypasses.
4. Expect eleven history rows, 33 application tables plus EF metadata, ten enabled user triggers and the expanded constrained model. Validate PK/FK/CHECK/unique relationships, all indexes/constraints, original primary-key sets, old column values and UTC appointment/resource intervals. Confirm default cutoff120 and legacy OwnerPhone/null-stage compatibility without converting or backfilling records.
5. Deploy matched Flutter/API builds together. An old client is not guaranteed to parse Customer-source appointments, and the new API must not run against schema7. Verify application-role privileges and health before reopening writers.
6. Perform explicitly authorized minimal owner/legacy-history/customer-booking/employee/countdown/completion/finance smoke checks. Preserve existing appointments; do not manufacture historical payments/revenue. Clearly label any intentional new test records and obtain a data-preserving cleanup decision if needed.
7. Reopen writes only after preservation and behavior checks pass. Record actual history, release SHA, backup references, health and test evidence; update Issues and reconcile the existing Kanban board through authorized Projects write access.

## Failure and recovery

The SQL has **four separate transactions**, not one atomic 7→11 operation. On failure, stop, retain maintenance mode and inspect actual committed history; never blindly repeat after an unexplained error. Keep the failed database and diagnostics.

Do not use EF Down as a data-preserving rollback. Execution/financial history may make downgrade lossy or explicitly blocked. Prefer a reviewed forward repair. Any recovery restore must first preserve/reconcile writes since the backup, restore into another isolated target, verify data and permissions, and obtain explicit approval before application switchover or overwriting anything.

The upgraded rehearsal copy `washflow_migration_rehearsal_20261127_b9eff1a879` contains synthetic smoke data and **must never replace the original demo**.
