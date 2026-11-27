# Ordered resource-stage increment

25 November 2026. Implements the approved resource-stage continuation of #4/#6/#8. Source changes passed final verification; no demo migration or GitHub push has occurred.

## Behavior

Owners configure a service/vehicle-category plan in Turkish: ordered names, active minutes, work area and required employee count. Stage estimates sum to the catalog's active duration; the highest concurrent staff requirement equals the catalog staff estimate. Configuration is versioned; replaced definitions are archived, not deleted. Confirmed future appointments block incompatible plan replacement/disablement and require explicit resolution.

Booking and preview validate each stage's full business-local opening interval, area capacity and required employees' full dated availability. Stage intervals are consecutive UTC intervals derived from the service's actual configured durations. Each area and employee is reserved only for that stage. A configurable combined service remains one booked catalog service with multiple execution stages; this increment does not introduce an arbitrary shopping cart of unrelated services.

For staged owner bookings, selected employees form a bounded pool (at least the maximum required concurrently; at most 100 distinct memberships). The server chooses eligible staff separately for each stage, rather than inferring eligibility from identifier order. A worker available only for a later stage does not wrongly prevent an earlier stage staffed by another eligible worker. Legacy single-stage bookings retain their exact-staff selection contract. No employee outside the authorized selected pool is assigned.

AppointmentStage snapshots preserve names, sequence, offsets, durations, areas and staff counts. Resource reservations retain stage identity. Catalog/template edits do not rewrite an existing booking; rescheduling validates all snapshotted stages before atomically replacing reservations. Failed rescheduling preserves original versions, resources and history. Cancellation releases all stage allocations. Same-appointment planned and actual occupancy is merged before counting capacity.

Owner work-order conversion uses the appointment's stored stages and per-stage employee reservations. Staff cannot change the reserved plan through work-order creation input. Employees see only assigned stages and cannot modify another stage merely because they share a work order. Execution checks its actual stage area; overdue/paused work protects that area and employees even after the original estimate. Historical cancelled work orders snapshot their original planned start so a later explicit appointment reschedule does not rewrite that audit display.

## Migration safety

`20261125090139_OrderedStageScheduling` adds ServiceStageTemplates and AppointmentStages, nullable reservation StageId, default-false requirement StagePlanEnabled, nullable execution-stage WorkAreaId and nullable WorkOrders.PlannedStartUtc. Existing records retain their values; legacy work orders fall back to their appointment's area/time when no new snapshot exists. Legacy reservations keep StageId NULL and their unique resource protection. Additional unique indexes protect stage-specific reservations and allow only one running/paused stage per work order.

Appointment stage snapshots and reservation history are immutable. Work-order identity/creation/planned-time snapshots and terminal timestamps are protected by a trigger; audit events remain append-only. All FKs use tenant/appointment ownership paths and RESTRICT. Index replacement occurs within the migration transaction. No table or data reset is used on upgrade. Downgrade refuses when new stage/planned-time data exists, requiring a separately reviewed preservation plan rather than silently dropping history.

This migration is tested only inside unique schemas on the disposable PostgreSQL 17 database. Existing demo records are untouched. Review the idempotent upgrade SQL and obtain explicit approval before demo deployment.

## Verification scope

PostgreSQL scenarios cover: independently usable exterior/interior areas, one/two-employee stages, an employee's shift starting only at the later stage, full required coverage, late shift rejection, concurrent last-slot requests, unchanged original after failed move, stage-preserving successful move/cancel, versioned archived configuration, initially missing requirement creation, foreign-area rejection without partial writes, worker-specific stage authorization, actual second-area occupancy after overrun and cancelled-order audit time stability. Existing scheduling, identity/privacy and lifecycle tests remain enabled.

Flutter tests exercise the actual plan editor's payload, per-area selection, duration validation, conflict/no-false-success feedback and stage-enabled booking contract. Feedback scrolls into view on long forms. Final complete-suite verification: **429 backend tests passed** (254 unit + 175 integration), **351 Flutter tests passed**, zero failures/skips. Flutter analysis and git diff --check passed. See TEST_MATRIX.md.
