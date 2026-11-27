# Final sprint delivery status

25 November 2026. The essential graduation demonstration now works end-to-end in an isolated native rehearsal. **The preserved demo database has not been migrated, and no PR has been merged.**

| Module | Verified delivery | Review reference |
| --- | --- | --- |
| Scheduling protection | Hours/timezone/shift edits cannot invalidate confirmed bookings; employee overlap, area capacity, stage order, concurrent requests and failed-move rollback covered | `40389ba`, PR #17; staged extension `b29fbc9`, PR #19 |
| Employee work orders | Assigned-only ordered start/pause/resume/complete, server timestamps/countdown, overdue/no automatic shift, owner monitoring, audit and explicit cancellation/replacement for reassignment | `40d4c9c`, PR #18; PR #19; real iOS rehearsal |
| Customer self-service | Real business discovery/prices/hours/slots, owned vehicle, common calendar, immediate confirmation, history/detail and permitted cancel/reschedule; default stored business cutoff 120 minutes | `945848e`, [PR #20](https://github.com/barisgundogdu/WashFlow/pull/20) |
| Owner finance | Completed-work payments/partial balances, configurable methods, categorized manual income/expenses, audited reversals/corrections, local-date reports and breakdowns | `210bb93`, [PR #21](https://github.com/barisgundogdu/WashFlow/pull/21) |
| Integrated demonstration | Real customer → owner calendar → two employee stages/countdown/overdue → partial payment + expense → exact report → customer history | [Native rehearsal](LIVE_REHEARSAL.md), four actual screenshots |
| Data/academic evidence | 33 application tables, 340 columns, 71 FKs, 11 migrations on a **new isolated target**; original dated demo inventory preserved | [Schema evidence](SCHEMA_EVIDENCE.md) |

## Verification and publication

- **445 backend passed** (254 unit + 191 integration), **370 Flutter passed**, no failures/skips in final module suites; static analysis clean. One additional full real-iOS/socket/API/database scenario passed. [Exact test matrix](TEST_MATRIX.md).
- PR #17/#18/#19 were inspected before continuing: open, unmerged, exact-head push/PR CI successful. Customer PR #20 and finance PR #21 also have successful exact-head push and PR CI. PRs are stacked in that order, targeting the preceding increment; review without automatic merge.
- CI Issue #12 acceptance is fully verified and the issue was closed completed. Feature issues retain original scope, verified progress and review/deployment limitations; no unfinished umbrella story is closed merely for a passing increment.
- Git preserved the existing work and initial untracked `.DS_Store`; nothing was reset or force-pushed. Customer and finance each have separate tested commits/PRs.

## Remaining work and readiness

The **essential demo is presentation-ready in the isolated rehearsal environment**, with real backend behavior and reviewed native screenshots. For the preserved existing demo target, first review the stacked PRs and select the integration commit; a verified backup and explicit user migration approval are still required before applying migrations 8–11. Older clients do not parse the new Customer booking source; deploy matching Flutter/backend versions together. No permission to migrate that database or auto-merge is inferred.

The 120-minute policy is stored per business and tested with a different business-specific value. An owner-facing policy editor is a future enhancement. Employee reassignment uses explicit work-order cancellation, authorized appointment staff update and a replacement order; previous execution history remains intact.

Broad walk-in/late-arrival automation, advanced employee analytics and external operational notification delivery remain outside the completed essential demonstration. Local memory email was verified; SMTP/push delivery was not. Physical devices/Android native runs and production deployment were not tested. Optional features do not replace the completed P0/P1 evidence.

## Kanban limitation and exact pending updates

Existing [WashFlow Development project #1](https://github.com/users/barisgundogdu/projects/1) was inspected. The connected GitHub tool exposes no Projects write action, and available IAB/Chrome sessions are signed out. **No direct board status update is confirmed.** This is separate from successful Issue/PR updates.

- #12 → DONE (CI issue actually completed/closed; re-read the board before asserting any automatic workflow move).
- #4 → DOING (broad scheduling story; walk-in/lateness scope still open).
- #11 and #7 → DOING/review of the verified customer and finance PRs.
- #6: verified work-order acceptance and native evidence are attached; queue its PR review when a DOING slot is available.
- Respect the three-card DOING limit. Existing broad #2/#3 cards require a truthful scope/status review before freeing their slots; do not silently raise WIP or mass-mark feature stories DONE. #1/#9 stay unchanged. #10 remains open for its broader profile-management scope; #14 retains its separate midterm demo-schema acceptance.

[Backlog](FINAL_BACKLOG.md), [audit](IMPLEMENTATION_AUDIT.md), [demo sequence](DEMO_CHECKLIST.md), [customer increment](CUSTOMER_BOOKING_INCREMENT.md) and [finance increment](FINANCE_INCREMENT.md) retain requirement/implementation/test traceability.
