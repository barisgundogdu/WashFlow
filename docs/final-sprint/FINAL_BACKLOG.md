# Final sprint backlog

25 November 2026. Reuse existing issues; no equivalent duplicate issue created. Local status is independent of remote board status. No feature is DONE merely because a design exists.

| Priority / issue | Deliverable and acceptance | Dependencies / tests | Local status |
| --- | --- | --- | --- |
| P0 #4 | Reject conflicting hours/timezone edits before writes; preserve original confirmed appointments; compatible edits succeed | Hours boundary, cancellation, reschedule rollback, staff-vs-area independence, PostgreSQL concurrent booking/configuration tests A–L | P0 increment verified (415 backend / 343 Flutter); broad #4 remains DOING locally |
| P1 #6, #8 | Tenant-scoped ordered work stages; employee assigned-only start/complete; actor audit, duplicate-safe requests, owner read-only progress; server timestamps and overdue countdown in Turkish Flutter UI | #4; lifecycle, privacy, HTTP, restart/resume countdown widget tests; architectural approval | TODO |
| P1 #11, #10, #4 | Database business directory/search/detail, published offerings/hours; owned vehicle selection; authoritative slots and same engine booking; own agenda/history; 2-hour cancel/reschedule default | #2/#3/#4, approved schema design; cross-source race, cross-customer, cutoff exact-boundary and rollback tests | TODO |
| P1 #7 | Owner-only payment, manual income and expense entry; explicit TRY decimals; cash/card/transfer, partial payment only with validated balance; date-range cash collection vs earned revenue summaries | Completed execution, additive ledger design; double-counting, tenant/role, totals and negative-input tests | TODO |
| P2 #6, #8 | Owner live execution/workload/occupancy and overdue conflicts; factual elapsed-versus-estimated durations | Work stages; independent roles/tenants and demo verification | TODO |
| P2 #6, #11 | Supported in-app operational events; inspect SMTP configuration before delivery claims | Execution/booking events; idempotence, production mailbox exclusion | TODO |
| P2 #9 (already closed; preserve), relevant feature issues | Polish each implemented vertical slice: Turkish errors/loading/empty states, responsive forms, accessible controls | Flutter analysis/widget tests and live simulator screenshots | TODO |
| P1 #12 | Verify full local suites and hosted CI for each pushed increment | Dedicated PostgreSQL; no skipped database verification | Existing CI implemented; final sprint checks TODO |
| P1 #1, #14 | Update requirements decisions/evidence, ERD after approved migrations, thesis actual-results section | Verified schema and demos; no false completion claims | TODO |
| P1 #1 | Rehearse final presentation using DEMO_CHECKLIST.md, collect runtime evidence | P0/P1 vertical slices; do not present placeholders as implemented | TODO |
| P3 #7/#8 | Additional reports/analytics beyond demonstration needs | All P0/P1 acceptance and tests first | Deferred |

## Concrete pending Kanban updates

Existing project: https://github.com/users/barisgundogdu/projects/1 . Read-only public browser; Projects mutation unavailable.

1. Move #4 TODO → DOING now (existing DOING #2/#3 leaves exactly three cards, matching visible WIP limit).
2. Keep #2/#3 DOING and #6/#7/#8/#10/#11 TODO until work actually starts or acceptance completes. Do not mark broad #4 DONE for the owner-only P0 increment.
3. Review merged PR #13 + latest hosted CI against #12 before moving DONE/closing; review PR #15 and committed ERD against #14 before DONE/closing.
4. #1/#9 remain DONE/closed. No unrelated issue closure.
5. When starting P1, resolve WIP capacity explicitly and attach reviewed technical scope/testing requirements to existing #6/#11/#7; avoid mass-editing original acceptance criteria.

## Suggested commit / PR split

- `fix(scheduling): preserve confirmed bookings during hours edits (refs #4)` — P0 fix + regression tests + audit docs, base feature/phase10-ui-ux.
- Separate work-order, customer booking and finance branches/PRs after architectural review. Include requirement IDs and exact validation results in each.
- Record the validated P0 local commit in the milestone response and issue progress update. No PR, push or merge is implied by a local commit.


## Remote synchronization performed

Issue descriptions #4, #6 and #11 were updated on 25 November 2026 to preserve the original acceptance criteria while adding P0 verification scope and explicit customer/overrun policies. All remain open. No Kanban mutation occurred; #4 → DOING remains the exact pending update. No milestone exists in the repository. Available labels were inventoried; no new label was created or blindly assigned.
