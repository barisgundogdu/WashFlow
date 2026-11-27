# Customer self-service increment

25 November 2026. Refs #11, #10, #4. Based on the verified ordered-stage increment (`b29fbc9`, PR #19). Architectural increment 2 and publishing verified branches were explicitly approved.

## Implemented behavior

- Authenticated directory/search/detail lists real database businesses with enabled, published services. Safe public DTOs include intended business contact/address, hours, timezone and prices, never the employee roster or other customers' account/history.
- A customer selects their own active vehicle, business, service, local date and available instant. Slot results are advisory; authoritative create uses the common business lock, hours, full staff availability, work-area capacity and ordered stage allocator. Owner phone bookings and customer bookings compete for the same resources.
- Available capacity immediately creates a Confirmed appointment. Existing Appointments is the sole calendar. Customer source uses CustomerProfileId/OwnedVehicleId and null local references. Composite ownership FKs and server checks prevent cross-customer use. Matching names/plates never auto-link existing tenant records.
- Customer upcoming/history/details and cancel/reschedule are profile-scoped. The server enforces the current original start minus the business cutoff, including equality. Default stored policy is 120 minutes per business; future configuration does not require engine changes. This increment does not expose a policy editor.
- Failed moves preserve the original reservation and version. An explicitly authorized customer move/cancel retires only an unstarted prepared work order with an actor event; active/completed execution blocks customer changes. Prior work-order planned-time history remains intact. Existing confirmed bookings are never moved automatically.
- Owner agenda parses both booking sources and retains price/name/plate/stage snapshots. Owner management remains tenant-scoped; customer booking does not grant browsing of unrelated profile information.
- Turkish Flutter discovery, hours, own-vehicle selection, price/duration confirmation, paginated slots, own history/detail/cancel/reschedule are connected to the API. A device-secure, profile-keyed pending create is written before sending. Uncertain outcomes retain the same request ID; explicit replay returns the current booking state rather than a stale success. Context generation and session revision discard late account-switch responses.

## Database and compatibility

Migration 10: `20261125130449_CustomerSelfServiceBooking`. Adds two nullable ownership columns, restrictive FKs, ownership indexes/alternate key and per-business cutoff default 120; relaxes the two local IDs only alongside a source-specific replacement check. Existing owner records retain every reference and timestamp. Immutable snapshot trigger automatically protects the added ownership fields. No completion, customer links, revenue or payments are invented. Downgrade refuses if customer history or a customized policy would be lost.

Generated and inspected offline SQL: `/tmp/washflow-customer-upgrade.sql`. Populated schema-9 upgrade regression preserves an owner appointment and both unreleased resource reservations. Tests run only against disposable schemas on the separate PostgreSQL service at port 55439. **No migration applied to the existing demo database.** Older app clients cannot parse customer-source rows; deploy the matching Flutter/backend versions together after approval.

## Verification

- Backend: 436 passed (254 unit, 182 integration), zero failed/skipped. Includes discovery/privacy, foreign vehicle/record denial, revoked sessions, safe repeated create/cancel, exact 2-hour boundary and one tick late, owner/customer last-employee race with spare area capacity, failed-move rollback, prepared-work history, per-business cutoff, unconfigured hours and populated upgrade/downgrade protection.
- Flutter: 358 passed, zero failed/skipped, including the owner-agenda customer-source parser regression. Analysis clean. New widgets exercise real selection/price/explicit confirmation, secure pending recovery and cutoff/late-response handling with controlled adapters.
- No live simulator/deployed demo claim is made at this checkpoint. Final rehearsal must use a separately approved migration target or disposable demo environment.
