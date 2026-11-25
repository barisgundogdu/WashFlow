# Employee work-order execution increment

25 November 2026. References #6/#8 and #4; FR-10/11/12, NFR-02/03/06/09. The user explicitly approved all three proposed architectural increments. Demo database migration and Git push remain separately unauthorized.

## Implemented behavior

- An authorized owner explicitly converts a confirmed appointment into a work order. One live/completed order per appointment; cancelled order history remains and an explicit replacement is possible.
- Owner supplies 1–20 ordered named stages. Their total estimate must equal the agreed appointment active minutes. This first execution increment inherits the appointment's reserved area and employees for all stages. It does not claim distinct area/staff templates per stage or multi-service scheduling; those require the next resource-stage increment before broad #4/#6 completion.
- Employee endpoints list/read only assigned work and permit start/pause/resume/complete only on assigned stages, in sequence. Owner can monitor and explicitly cancel work; owner-only role cannot perform employee stage actions.
- Server timestamps, accumulated paused milliseconds, UTC instants and serverNow support remaining/overdue displays. Countdown expiration never completes work. Flutter uses a monotonic elapsed clock, refreshes on resume/reconnection requests and every 30 seconds while foregrounded; the one-second timer updates display only.
- Running/paused work retains actual occupancy. A common ResourceCapacity calculation combines actual and planned use, merging the same appointment's area intervals to avoid double counting. Past estimated ends do not silently release employees. Booking and execution both use this helper under the existing business lock.
- Explicit owner cancellation resolves interrupted work, retaining start timestamps and audit events. To reassign: cancel the work order, explicitly reschedule the appointment's employees, then create a replacement. No automatic movement of confirmed appointments.
- Current/overdue work blocks incompatible hours changes, shift edits, area archival and capacity reductions. Bound work orders prevent appointment cancellation/rescheduling until explicitly resolved; completed service history cannot be silently rewritten.
- Request IDs, fresh session/membership checks, xmin and transactions prevent duplicate actions/lost updates. An injected event-save failure rolls back stage/root changes. Event history is database append-only.

## API surface

All routes begin `/api/v1/businesses/{businessId}` and require authenticated current authorization. Responses are no-store. Errors use the existing safe Turkish Problem Details contract.

| Method | Path | Authorization |
| --- | --- | --- |
| GET | /owner/work-orders?page=1 | Owner, tenant scope, 30/page |
| GET | /owner/work-orders/{id} | Owner, tenant scope |
| POST | /appointments/{appointmentId}/work-order | Owner; request ID, expected appointment xmin, named stage estimates |
| POST | /owner/work-orders/{id}/cancel | Owner; request ID and work-order xmin |
| GET | /employee/work-orders?page=1 | Employee, assigned-only |
| GET | /employee/work-orders/{id} | Employee, assigned-only |
| POST | /employee/work-orders/{id}/stages/{stageId}/{action} | Assigned Employee; start/pause/resume/complete, request ID and work-order xmin |

Employee responses omit payment/pricing data and owner audit event lists. Public identifiers never confer authorization.

## Database safety

Migration `20261125082321_WorkOrderExecution` adds WorkOrders, WorkOrderStages, WorkStageAssignments and WorkOrderEvents. Composite tenant FKs, restrictive delete relationships, unique live-order/request indexes, stage/lifecycle checks and append-only event trigger protect integrity. Existing tables/rows are not rewritten. EF model snapshot is checked against the current model. Existing fixture test model counts were updated for four explicitly added entities/eighth migration; no failing invariant was removed.

This migration has been applied only to uniquely named schemas in the dedicated disposable PostgreSQL 17 test database at loopback port 55439. The existing demo database remains untouched. Generate/review an idempotent migration script and obtain explicit approval before any demo deployment. No EnsureDeleted, reset or production seed exists.

## Flutter integration

New work-order adapter/domain and list/detail/create screens are wired through the existing optional app/router/session dependency boundaries. Owner navigation opens work orders; Employee navigation opens assigned work. Named stages are editable before creation, with total-duration validation. Actual data comes from the backend. Owner progress, pending/active/completed filters, business-local planned time, explicit confirmation, safe errors, countdown and overdue warnings preserve Turkish UI and existing branding.

## Verification

Targeted tests already pass: five real PostgreSQL API tests and two domain lifecycle tests; five Flutter adapter/countdown/widget cases. API tests cover ordered execution, pause/overdue/retry, owner/employee/foreign-tenant boundaries, concurrent actions, explicit interrupted-work resolution, immutable history, actual-vs-planned capacity deduplication, overdue staff conflicts and transactional failure rollback. Final full-suite results: 422 backend tests passed (254 unit/168 integration), 348 Flutter tests passed, Flutter analysis and git diff --check passed; zero failures/skips. See TEST_MATRIX.md. No live simulator demo or delivered operational notification is claimed.
