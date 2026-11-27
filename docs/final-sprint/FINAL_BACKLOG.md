# Final sprint backlog

25 November 2026. Reuse existing issues; no equivalent duplicate issue created. Local status is independent of remote board status. No feature is DONE merely because a design exists.

| Priority / issue | Deliverable and acceptance | Verification | Local status |
| --- | --- | --- | --- |
| P0 #4 | Confirmed booking protection during hours/timezone/staff edits; capacity and concurrency safety | PostgreSQL A–L regressions; native incompatible-hours rejection | Verified; broad walk-in/lateness scope open |
| P1 #6, #8 | Assigned ordered employee work, actual timestamps, countdown/overdue, owner monitoring and audit | API/widget tests + real iOS two-stage execution/overdue/reopen | Implemented and rehearsed; PR review pending |
| P1 #11, #10, #4 | Real discovery, own vehicle, common-calendar booking/history/change policy | 436 backend/358 Flutter at customer commit; real iOS booking/history | Implemented and rehearsed; PR #20 review pending |
| P1 #7 | Payments, partial balance, manual income, expenses and reliable reports | 444 backend/370 Flutter final suites; native payment/expense/report | Implemented and rehearsed; PR #21 review pending |
| P2 #6, #8 | Owner execution and alerts; broader workload/performance analysis | Owner native warning/progress verified | Core monitoring done; advanced metrics deferred |
| P2 #6, #11 | Supported operational notifications; truthful provider claims | Local verification/invitation dispatch used in live setup | External operational delivery deferred/unverified |
| P2 #9 (closed; preserve) | Consistent Turkish loading/errors/forms and native layout | Analysis clean; 370 Flutter tests; four native screenshots reviewed | Core vertical slices verified; no wholesale redesign |
| P1 #12 | Push/PR CI with PostgreSQL and Flutter | All exact heads #17–#21 successful; runner-local credentials | DONE; CI issue closed completed |
| P1 #1, #14 | Requirements, source-schema ERD, thesis evidence | New isolated inventory: 33 tables/71 FKs/11 migrations; historical demo kept | Updated; final thesis integration/review remains |
| P1 #1 | Real end-to-end final rehearsal | One iOS real socket/backend/database test passed in 2m25s | Essential demonstration verified; preserved-demo deployment needs approval |
| P3 #7/#8 | Additional analytics/reports/integrations | P0/P1 first | Deferred |

## Initial pending Kanban checkpoint (historical)

Existing project: https://github.com/users/barisgundogdu/projects/1 . Read-only public browser; Projects mutation unavailable.

1. Move #4 TODO → DOING now (existing DOING #2/#3 leaves exactly three cards, matching visible WIP limit).
2. #6 execution work is now active locally. Before moving #6 TODO → DOING, respect the board WIP limit of three: #2/#3/#4 currently occupy those slots; review inactive umbrella cards rather than silently raising the limit. Customer #10/#11 is now active; #7 follows after customer validation. Do not mark broad #4 DONE for the owner-only P0 increment.
3. Review merged PR #13 + latest hosted CI against #12 before moving DONE/closing; review PR #15 and committed ERD against #14 before DONE/closing.
4. #1/#9 remain DONE/closed. No unrelated issue closure.
5. When starting P1, resolve WIP capacity explicitly and attach reviewed technical scope/testing requirements to existing #6/#11/#7; avoid mass-editing original acceptance criteria.

## Suggested commit / PR split

- `fix(scheduling): preserve confirmed bookings during hours edits (refs #4)` — P0 fix + regression tests + audit docs, base feature/phase10-ui-ux.
- Separate work-order, customer booking and finance branches/PRs after architectural review. Include requirement IDs and exact validation results in each.
- Record the validated P0 local commit in the milestone response and issue progress update. No PR, push or merge is implied by a local commit.


## Remote synchronization performed

Issue descriptions #4, #6 and #11 were updated on 25 November 2026 to preserve the original acceptance criteria while adding P0 verification scope and explicit customer/overrun policies. All remain open. No Kanban mutation occurred; #4 → DOING remains the exact pending update. No milestone exists in the repository. Available labels were inventoried; no new label was created or blindly assigned.


Employee execution core verified in local commit `40d4c9c` (422 backend/348 Flutter passed). Ordered per-stage resource allocation is being finalized on `codex/staged-scheduling`; its full acceptance and latest test evidence are tracked separately. Customer booking is now implemented and verified in its focused increment; finance follows after publishing its PR. Both available browser surfaces are signed out, confirming the Kanban write limitation.


Ordered area/staff scheduling and employee execution are now verified together: 429 backend / 351 Flutter tests passed; analysis clean. The approved one-booked-service/multiple-ordered-stages flow is working in source and API/widget tests. Keep broad #4/#6 open while customer history, final live rehearsal and GitHub publication are pending. Next implementation: #11 customer self-service on the same scheduling engine, with approved 120-minute cancellation/rescheduling default.


Customer self-service is implemented on `codex/customer-self-service`; see CUSTOMER_BOOKING_INCREMENT.md. Backend 436 passed with no skips; Flutter final verification is recorded in TEST_MATRIX.md. PRs #17/#18/#19 were rechecked: push and PR CI succeeded on their exact recorded heads, all remain open/unmerged. No existing work or demo data reset. Customer #11/#10 should reflect DOING pending final rehearsal; do not close broad issues from automated evidence alone.


## Essential module delivery checkpoint

Customer PR [#20](https://github.com/barisgundogdu/WashFlow/pull/20), commit `945848e`: 436 backend/358 Flutter passed; hosted push and PR CI successful. Finance now implemented and verified on `codex/owner-finance`: 444 backend/370 Flutter passed, analysis clean; see FINANCE_INCREMENT.md. Employee/stage/customer/finance modules all have connected APIs and Turkish Flutter screens. Remaining essential verification is the integrated live rehearsal and deployment review; no demo migration or merge. Optional advanced employee analytics and external operational delivery remain deferred. Keep broad Issues open until their full acceptance/rehearsal is satisfied. Exact current board intentions: #4/#6/#11/#10/#7 reflect implementation/rehearsal progress; resolve three-card WIP via existing umbrella card review rather than raising the limit. Projects write still unavailable.


## Final board actions

See FINAL_STATUS.md for the current exact pending board updates and WIP constraint. Issue/PR synchronization succeeded; direct Projects writes remain unavailable. CI #12 is completed/closed, while #4/#6/#7/#10/#11 remain open with verified implementation and review/scope notes. Do not mistake historical TODO checkpoint text above for the current source implementation.
