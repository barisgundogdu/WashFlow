# Final sprint implementation audit

Audit date: 25 November 2026. Baseline `d3f97d6`; Issues and source inspected before implementation. SRS v0.2 remains a draft; this report distinguishes code evidence from runtime verification.

## Repository and integration

- Default branch verified through GitHub: `main`.
- Active development baseline: `feature/phase10-ui-ux`, 18 commits ahead of `origin/main`, zero behind after successful `git fetch origin`.
- Focused local branch: `codex/final-sprint-scheduling`, based on the active development baseline. Integration target for this increment is the development branch; do not target stale `main` without reviewing the missing 18 commits.
- Initial untracked `.DS_Store` preserved. No user source edits found. No database reset, demo migration, push or merge performed.
- Existing PRs #13 (CI), #15 (live ERD), #16 (thesis) are merged/closed. No open PR returned by the repository issue collection.
- #9 acceptance criteria read: closed UI foundation task, already implemented. Do not restart it.

## Requirements and source evidence

| Workstream | Actual evidence | Gap / delivery priority |
| --- | --- | --- |
| Identity / tenant access (#2, #8) | JWT, sessions, email verification, context selection, invitations; backend and Flutter tests | Employee lifecycle management and assigned-work authorization incomplete; P1 |
| Customers / vehicles (#3, #10) | Separate `OwnedVehicle` and tenant-local records; owner CRUD, customer vehicles, scoped authorization | Customer profile editing, appointment history and verified linking incomplete; no plate-based linking permitted |
| Catalog (#5) | Service offerings with decimal TRY price, active/waiting minutes, required staff, publication/archive | Ordered resource stages absent; catalog alone does not complete work-order requirements |
| Owner scheduling (#4) | API, owner agenda/configuration/booking/detail screens; `EfSchedulingService`, business row locks, active reservations, xmin, idempotent receipts, cancellation/rescheduling events | Only OwnerPhone source, Confirmed/Cancelled lifecycle, explicitly single-stage service requirements; no walk-in execution |
| Hours / staff (#4) | `WithinHours` uses business IANA zone; full shift coverage; peak area capacity; same employee blocked across areas; dated availability changes preserve reservations | **Confirmed defect:** `EfBusinessSettingsStore.UpdateAsync` replaces hours/timezone without checking existing appointments. P0 fix required |
| Employee execution (#6, #8) | Employee membership and availability exist | Employee home explicitly temporary; no WorkOrder/Stage persistence, execution API, countdown or reassignment audit; P1 |
| Customer booking (#11) | Owned vehicles and known-business published catalog are usable building blocks | No directory, slot search, customer booking ownership/source or customer agenda; customer home explicitly says unavailable; P1 |
| Finance (#7) | No payment/expense/report entities or endpoints in source | Full owner financial vertical slice required; no current revenue claims; P1 |
| Owner dashboard | Working navigation to owner operations | No live work-order occupancy, financial dashboard or workload metrics; P2 |
| Notifications | Memory development sender, SMTP implementation, verification/invitation infrastructure | No operational notifications/outbox/push; delivery provider not verified. Development mailbox is environment/loopback guarded |
| Database / ERD (#14) | Seven additive migrations; latest `20261027125056_SchedulingPersistencePhase9B`; tenant composite FKs, xmin, snapshot/history triggers | `docs/database/live-schema.json` and ERD record 15 Nov capture (23 app tables, 38 FKs); no new live database audit claimed |
| Academic / CI (#1, #12) | Thesis sources/PDF and screenshots; backend+PostgreSQL and Flutter CI workflow | Final thesis must describe missing features accurately and collect new CI/demo evidence |

## Scheduling investigation

Existing engine correctly checks hours and employee coverage independently, combines adjacent availability windows without bridging gaps, uses half-open resource intervals, releases cancelled resources and preserves reschedule snapshots. Booking and scheduling configuration operations acquire the business row lock before authoritative reads. Settings updates use the same lock, making it possible to serialize hours edits with bookings without a new architecture.

Confirmed P0 defect: the settings path can close a day, shrink hours, clear hours or change timezone while confirmed future work remains booked. Proposed behavior: reject the settings update with HTTP 409 and Turkish explanation if any confirmed appointment whose active end is in the future no longer fits the proposed hours/zone. Keep appointment, business profile, hours and versions intact. Resolve by an authorized explicit reschedule/cancel first. Compatible edits remain allowed. Reuse existing scheduling rules; no migration needed.

Known limits: waiting/drying does not occupy constrained resources in the current single-stage model. No separate breaks/time-off entity; gaps in dated availability represent unavailable periods. Employee conflicts are currently tenant scoped; cross-business employment policy is unresolved. No proof of multi-stage or customer booking concurrency until those flows exist.

## GitHub and Kanban audit

[WashFlow Development #1](https://github.com/users/barisgundogdu/projects/1) was read in the public browser. Views: Backlog, Priority board, Team items, Roadmap, My items. Visible Status values: TODO, DOING, DONE; Estimate displayed; DOING WIP limit 3. Priority field options not inspected.

- TODO: #4, #5, #6, #7, #8, #10, #11, #12, #14.
- DOING: #2, #3.
- DONE: #1, #9.
- Repository issue states: #1/#9 closed; all other above issues open. Issue records have no labels or milestones assigned.
- Connector label/milestone collection calls were unsupported. Read-only public REST fallback verified labels: accessibility, bug, documentation, duplicate, enhancement, good first issue, help wanted, invalid, question, wontfix. No milestones exist. No priority labels are defined; project Priority field options remain unverified.
- Connector has issue/PR tools but no Projects mutation tool. Browser is signed out and cards disabled. **No Kanban update succeeded or is claimed.** Exact pending changes are in FINAL_BACKLOG.md.
- #12/#14 have merged evidence while remaining TODO/open. Do not close them without checking their full acceptance evidence.

## Baseline validation

- Backend: 252 unit + 38 non-PostgreSQL integration tests passed; 108 PostgreSQL tests skipped because WASHFLOW_TEST_POSTGRES unset. Total discovered 398. Initial sandbox run could not bind the test runner socket; rerun with approved execution succeeded.
- Flutter: 341 tests passed. Initial sandbox attempt could not update SDK cache; approved rerun succeeded.
- Docker initially stopped; isolated PostgreSQL validation is being prepared. No skipped test counts as PostgreSQL proof.

## Confirmed sprint policy decisions

User confirmed 25 November 2026: customer bookings automatically confirmed after authoritative capacity validation. Customer cancellation/rescheduling allowed until two hours before the original start (inclusive boundary); future per-business configuration must be supported. Keep customer policy separate from owner operations. No automatic movement of existing appointments when execution overruns; show overdue time and warn owner of potential conflicts. Existing confirmed appointments remain intact unless explicitly changed by an authorized user. Hours/availability edits must detect conflicts and require resolution. New sprint request explicitly authorizes the previously later-scope employee metrics and supported operational notifications, after P0/P1.

## Architectural review gate for P1

AGENTS.md requires approval before significant architectural changes. Extending appointment lifecycle/source and its database triggers, adding ordered work execution, customer-owned booking association and financial ledgers are significant changes. Prepare a concrete design with additive migration and authorization boundaries before seeking approval. P0 uses existing design and proceeds independently.


## P0 implementation progress

The five incompatible settings regression cases failed against the original code (5 failed/0 passed) on isolated PostgreSQL 17, reproducing the defect before the fix. The store now checks all not-yet-ended confirmed intervals against proposed hours/timezone under the existing business lock, before any profile/hour mutation. Incompatible changes return HTTP 409 code `appointmentsOutsideHours`. A distinct safe Flutter error explains explicit agenda resolution, retains the editable draft and does not present success. Reused stores clear stale EF tracked state before reauthorization/version checks; a separate regression verifies stale editor rejection.

New regression coverage includes hours/shift-vs-booking concurrency with independent connections, same employee across different areas, multiple required employees, exact closing and short-shift boundary, cancelled capacity, already-started confirmed intervals, local/UTC date disagreement, and unchanged appointment/events/reservations/settings version on rejected edits. Existing concurrency/reschedule/rollback tests remain.

Baseline rerun on dedicated disposable PostgreSQL 17 container (loopback port 55439, tmpfs storage): all 398 backend tests passed (252 unit, 146 integration), no failures/skips. Existing demo container/data not queried or migrated. Final expanded-suite results are recorded in TEST_MATRIX.md.

GitHub #4/#6/#11 bodies updated successfully with P0 scope and confirmed execution/booking policies, preserving original acceptance criteria and open states. No duplicate issues or premature DONE changes. Board mutations remain unavailable.


P0 final verification: **415 backend tests passed (252 unit, 163 integration), 0 failed/skipped; 343 Flutter tests passed; Flutter analysis and git diff --check passed.** Seventeen backend and two Flutter cases added. No migration, push, PR or merge. The scheduling issue remains broader than this completed increment; full work orders/customer booking/finance await the documented architectural approval. All new test schemas were confined to the dedicated disposable database.


## Approved P1 continuation

The user approved all three architecture increments on 25 November 2026 and explicitly prohibited applying migrations to the demo without further approval. Employee execution is now implemented in a focused working increment; see WORK_ORDER_INCREMENT.md. Customer booking, finance and distinct-resource ordered-stage allocation remain outstanding. P0 local commit: `40389ba`; no push/PR. Work-order changes are not yet represented by a delivered commit while final validation runs.


The ordered-stage continuation adds independently configured areas/staff and immutable stage snapshots, plus eligible per-stage selection from an owner-selected staff pool. See ORDERED_STAGE_INCREMENT.md. The Chrome browser connection was additionally checked and is also signed out; public cards remain disabled. Projects writes are still unavailable, so exact pending changes remain documented rather than claimed as applied. Employee core local commit `40d4c9c` is verified; the staged increment remains under final verification at this checkpoint.


Ordered-stage final evidence: 429 backend and 351 Flutter passed, no failures/skips; Flutter analysis and whitespace check clean. Migration 9 preserves existing rows and stage histories, with downgrade guards. Employee-stage functionality is ready for the next customer-booking increment; demo deployment is still explicitly unapproved.


## Customer booking checkpoint — 25 November 2026

Customer self-service now uses the existing calendar and shared resource allocator, with profile-owned vehicles, safe public discovery, server-selected employees, two-hour default policy, own history and connected Turkish Flutter screens. See CUSTOMER_BOOKING_INCREMENT.md for migration 10 and verification. Existing demo schema remains untouched. PR #17/#18/#19 push and PR workflows were rechecked successful; no PR merged. The initial untracked `.DS_Store` remains preserved.
