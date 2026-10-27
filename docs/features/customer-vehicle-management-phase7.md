# Customer and vehicle management — Phase 7

Traceability: Issue #3; SRS FR-02/03/04/05/21/27/28, US-01 and the owner-side milestone A.
Baseline: `main`, `36fc2872f3ddcf8be14ae88f158f0625e0918de1`; initially clean. Requirements remain
specification; this phase implements customer/vehicle management, not booking or work orders.

## Implemented scope and ownership

There are two separate, non-linking models:

- `Business` → `LocalCustomer` → `LocalVehicle` for phone/walk-in operational records. A local
  customer has a required Business ID, full name, optional phone/email/notes, stable UUID and UTC
  timestamps. No User/CustomerProfile reference exists on this model.
- Existing `User` → existing `CustomerProfile` → `OwnedVehicle` for platform-wide personal vehicles.
  The profile is found from the authenticated account, never a body or route user/profile ID.

Separate physical vehicle tables avoid nullable ownership combinations. Local vehicles have required
`BusinessId` and `LocalCustomerId`, with a composite FK to `(BusinessId, Id)` on LocalCustomers.
This prevents assigning a vehicle to a customer in a different business, even through direct SQL.
Owned vehicles have one required CustomerProfile FK. All FKs use Restrict, with no cascade deletion.
Both tables have stable vehicle UUIDs, display and normalized search plates, required brand/model/type,
optional model year/color/notes, UTC created/updated timestamps, xmin versions and nullable archive time.
Category and year constraints also exist in PostgreSQL. No customer account is created by these APIs.
A phone/email/name/plate match is never ownership proof, a link, or a request to reuse another record.

Vehicles can be archived after explicit confirmation; the row and ID remain for future references,
while normal lists/searches and editing exclude it. Archive is not physical deletion, not an undoable
operation, and does not release ownership to another account. No customer-delete, hard-delete, transfer,
merge, account-linking or unarchive endpoint is implemented. Future reference tables must use stable IDs
and restricted FKs, and must reject selecting archived vehicles for new bookings/work orders.

## API contracts

All endpoints are authenticated, versioned, `Cache-Control: no-store`, and use the existing per-IP
authentication rate limit (60/minute). Every operation rechecks current scope on the server.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/v1/businesses/{businessId}/customers` | Customer page, optional search |
| POST | same | 201 customer |
| GET | `.../customers/{customerId}` | 200 customer detail |
| PUT | same | 200 updated customer |
| GET | `.../customers/{customerId}/vehicles` | Vehicle page for this exact parent |
| POST | same | 201 vehicle |
| PUT | `.../customers/{customerId}/vehicles/{vehicleId}` | 200 updated vehicle |
| POST | same + `/archive` | 204 |
| GET | `/api/v1/businesses/{businessId}/vehicles` | Vehicle page across this business's local customers |
| GET | `/api/v1/me/vehicles` | Authenticated Customer's own active vehicle page |
| POST | same | 201 owned vehicle |
| PUT | `/api/v1/me/vehicles/{vehicleId}` | 200 updated owned vehicle |
| POST | same + `/archive` | 204 |

Customer input: `{fullName, phone?, email?, notes?, version?}`. Full name 1–200 characters, phone ≤40
ASCII digits/common phone punctuation, email ≤320 address-only syntax, notes ≤2000. Optional empty
strings become null. Strings are trimmed and normalized to Unicode NFC. Unsafe controls are rejected;
notes permit line breaks/tabs. Unpaired/supplementary UTF-16 surrogates are currently rejected by the
server's conservative input validation; ordinary Turkish and BMP international letters are supported.

Vehicle input: `{plate, brand, model, category, modelYear?, color?, notes?, version?}`. Plate 1–32
characters with a letter/digit, brand 1–80, model 1–100, color ≤60, notes ≤2000. Optional year is an
integer 1886–2100. Category is exactly `Car`, `Suv`, `Van`, `Pickup`, `Motorcycle`, or `Other`; these
are stable classification labels, not pricing/duration rules. Turkish UI labels are supplied separately.

PUT replaces editable fields and requires the positive uint32 `version` returned by GET/list. Ownership
IDs cannot change. Archive body is `{version}`. Creation ignores any supplied version/ownership extras;
route and authenticated identity determine scope. Version mismatches are 409 `recordConflict`, without
partial changes or automatic overwrite. Version is an opaque PostgreSQL xmin snapshot, not a grant.

Customer response: `id`, `businessId`, `fullName`, nullable `phone/email/notes`, `version`, `createdAt`,
`updatedAt`. Vehicle response: `id`, nullable `businessId/localCustomerId/customerName` (local-only),
`plate`, `brand`, `model`, `category`, nullable `modelYear/color/notes`, `version`, timestamps. No user
or CustomerProfile IDs, normalized search keys or archive metadata are exposed in active lists.

List query: `q` ≤100 characters, `page` 1–10000, `pageSize` 1–50; defaults 1/20. Response is
`{items, page, pageSize, hasMore}`. Fetch size+1, not an unbounded total count. Owner customer search
matches normalized name, dial digits, or associated active local-vehicle plate. The business vehicle
list also accepts customer name/phone. Personal vehicle search matches only that account's plates.
Search values are parameterized and LIKE wildcard characters are treated literally by EF string
Contains translation. Stable sorting is normalized name/plate then UUID, so duplicate plates remain
separate, deterministic rows. Offset pagination is not a multi-request database snapshot: concurrent
inserts/edits may move rows between pages. Refresh to reconcile.

400 = malformed/invalid payload/query; 401 = invalid session; 403 = no active Owner grant for the
business or no CustomerProfile for `/me`; 404 = missing/inaccessible nested record in an authorized
scope; 409 = stale edit/archive; 429 = rate limit. Foreign and missing nested IDs use the same 404.
An unauthorized business uses 403 without revealing whether a record exists. Validation errors may be
returned before record lookup, as in existing APIs; they reveal no stored customer values.

## Plate and search policy

Recognized ordinary Turkish plate shapes use province `01`–`81`, 1–3 ASCII letters and 2–4 digits.
They display as `34 ABC 123`, independent of input case/spacing. Other formats are not coerced into a
Turkish pattern: foreign punctuation/letter case is preserved in display, with NFC and collapsed
whitespace. This is normalization, not legal registration validation or a country/checksum service.
Special diplomatic/temporary formats can use the generic plate representation if within safe bounds.

Search keys remove insignificant whitespace and use invariant uppercase; display text is separate.
Name search additionally folds Turkish dotted/dotless I variants; other diacritics remain significant.
Phone search strips dial punctuation and only treats digit/phone-punctuation queries as phone searches,
so a name containing a digit does not accidentally match every phone containing that digit.

**Duplicate plates are allowed everywhere**, including twice for one account/customer and across
businesses. A plate can change or be reissued; it is never a global permanent vehicle key. The UI shows
brand/model/customer information and a stable record-ID suffix; the editor exposes the full record ID.
Users select a specific record. Future appointment selection must use IDs plus verified ownership,
not a plate/name/email match. No cross-business customer search or public customer directory exists.

## Authorization and concurrency

| Principal | Local customers/vehicles | Own platform vehicles |
| --- | --- | --- |
| Active Owner of route business | Read/search/create/update/archive vehicles | Only with own CustomerProfile |
| Owner of another business | Denied | Only with own CustomerProfile |
| Employee-only grant | Denied | Only with own CustomerProfile |
| Platform Customer | Denied | Own profile only |
| Anonymous/revoked session | Denied | Denied |

Owner requests use the existing Business → membership → Owner-grant transaction lock order, shared
with settings/invitations. Mixed Owner+Employee grants still authorize Owner requests on the backend;
selecting the Employee Flutter experience does not expose management navigation. Multiple businesses
owned by one user remain independent. Account-owned operations lock the actor's CustomerProfile and
scope every query/update/archive to it. Requests cannot change owner, parent, business or profile.
Writes and version checks are atomic. Coarse per-business/profile locks favor correctness for the pilot;
future high-throughput tuning must preserve authorization/version guarantees and lock ordering.

No personal fields, bodies or search terms are logged by feature code. ASP.NET Hosting.Diagnostics
request-start/end logging is filtered to Warning because standard information logs include query
strings. EF sensitive-data logging remains disabled. Deployment must also redact queries/bodies at
reverse proxies, observability, crash reporting and external access logs; do not re-enable sensitive
parameter logging. Frontend errors use safe Turkish messages, never server response bodies.

## Flutter workflow and state safety

Owner home → **Müşteriler ve araçlar** revalidates the server context before opening a private tenant
navigator route. The customer tab has debounced (350 ms) name/phone/plate search, pages, empty/loading/
error/retry states and creation. Customer detail loads actual fields and associated vehicle pages,
with customer editing and vehicle creation/editing/archive. The vehicle tab searches the entire
business and opens existing vehicles; new vehicles require selecting a customer first.

Customer home → **Araçlarım** revalidates the Customer context, then uses `/me/vehicles`. It supports
multiple personal vehicles, plate search, edit and confirmed archival. No account-owned customer context
is inferred from an Owner/Employee label; it comes from `/auth/contexts` and is checked again on the API.

Editors show validation and unsaved state; back/cancel requires discard confirmation. Duplicate submits
are blocked synchronously. Conflicts/not-found and uncertain writes lock the editor and require returning
to a freshly loaded list and deliberately reopening a record. A cancelled uncertain create may have
committed: inspect the list before adding again. There is no hidden retry, including PUT/archival.
Confirmed writes close the form and reload authoritative GET state; a failed read never claims the
list refreshed. Detail refresh invalidates old vehicle requests and clears previous rows on failure.

Search generations, disposed controllers, identity revisions before/after API calls and server-context
scope generations reject stale responses. Role/business/account changes, logout and app suspension
destroy the entire private navigation tree, including forms/dialogs. Unsaved drafts/searches are memory
only and intentionally discarded on suspension/logout. Returning after resume revalidates contexts.
Existing serialized refresh-token safeguards, invitations and settings remain unchanged.

## Migration safety

Migration: `20261027074840_CustomerAndVehicleOwnership` (fifth migration, 14 model tables).
It only creates LocalCustomers, LocalVehicles, OwnedVehicles, their checks/FKs and scoped indexes.
No existing rows/tables are rewritten or linked; normalized keys are generated only for new records.
The [reviewed SQL](../../backend/docs/migrations/20261027074840_CustomerAndVehicleOwnership.sql) is retained.
Only a separate `washflow-phase7-test` PostgreSQL container with tmpfs storage and its own loopback
port/database was migrated. Test fixtures create/drop only uniquely named schemas there. The clean
full five-migration chain is exercised by existing migration/model tests, including the new tables;
previous-version upgrade tests and all earlier onboarding/invitation/settings tests are also run.
No existing development or production database was migrated, reset, truncated, deleted or overwritten.

Safe later deployment: back up the intended database, inspect its applied migration history privately,
generate SQL from its **actual** last applied migration, test a copy and obtain deployment authorization.
The developer database may still lack Phase 6; do not run just the Phase 7 incremental SQL in that case.
Use EF's complete pending chain (Phase 6 and Phase 7 as appropriate), review it, then apply only under
explicit authorization. New code should not be deployed against an unmigrated schema. Rollback drops
Phase 7 tables and loses those new records; use a backup/recovery plan rather than assuming reversal
preserves customer history. No migration/deployment against existing databases was performed here.

## Tests, limitations and next steps

Final executed counts and the complete file inventory are appended below. Backend PostgreSQL tests use
actual endpoint/auth/session/store pipelines via in-process HTTP; simulator tests use real socket
Kestrel, the actual Flutter adapters/UI and iOS Keychain. Live fixtures use the Development memory
mailbox only, not internet email delivery. No Android, physical-device or signed-release test is claimed.

Tests cover validation, Turkish/foreign plates, scoped phone/name/plate search, pages/limits, local and
personal CRUD/archive, forged tenant/parent/user/profile IDs, cross-model vehicle IDs, restricted FKs,
concurrent edit conflicts, multi-business Owner separation and removed grants. Flutter covers contracts,
UTF-8/query encoding, safe errors, no retries, duplicate submits, debouncing, stale responses, role and
account switching, lifecycle/dialog cleanup, keyboard/large-text layouts and prior phase regressions.

Known limits: substring search still scans the selected tenant's candidates (no trigram extension or
large-scale benchmark); offset pagination can shift during concurrent changes; no customer archive/
delete, transfers, linking, unarchive, permanent erasure policy, plate-registration verification or
bulk import. Phone/email are contact text, not verified identities. Archive preserves data rather than
satisfying a privacy-erasure workflow. Proxy log redaction and physical Android/iOS release checks remain
manual deployment work. These are scoped limitations, not claims of production readiness.

Next: Phase 8 service catalog, then appointment/work-order selectors using the stable returned customer/
vehicle IDs. Recheck business membership, parent ownership and active archive state transactionally when
booking; keep account-owned and tenant-local references explicit, and prevent cross-tenant history
exposure. Define scheduling capacity/DST/cancellation rules separately; no such feature exists here.

Manual checklist: two real Owner sessions conflicting on edits; physical-device keyboard/search/accessibility;
network loss during create/archive followed by deliberate list reconciliation; account/role/business switches
with requests in flight; review logging at the deployment proxy; back up/rehearse pending migrations on a copy.

Live opt-in command from `mobile` (only after privately verifying isolated backend/database identity):

```sh
flutter test integration_test -d <ios-simulator-id> \
  --dart-define=PHASE5_API_URL=http://127.0.0.1:5189
```

The existing Phase 5 test helper/define is reused, not production configuration. Use disposable accounts,
fresh signing material, a Memory email provider and suppressed sensitive logs; never put secrets in command
arguments or reports. The harness constructs WashFlowApp with real adapters, not the release main bootstrap,
and clears its app test session. Do not run alongside a personal signed-in app session.

## Executed validation — 27 October 2026

| Gate | Final result |
| --- | --- |
| `dotnet build backend/WashFlow.sln` | Passed, 0 warnings/errors |
| Backend unit tests | 101 passed, 0 failed, 0 skipped (26 new cases) |
| Backend integration tests | 95 passed, 0 failed, 0 skipped: 37 non-PostgreSQL + 58 PostgreSQL (8 new cases) |
| `flutter analyze` | Passed, no issues |
| `flutter test` | 220 passed, 0 failed, 0 skipped (42 new cases) |
| Live iOS simulator / Kestrel / PostgreSQL | All 5 distinct scenarios passed together; 2 Phase 7 scenarios repeated against the final rebuilt backend |
| `git diff --check` | Passed |

The combined live run completed in 3 minutes 15 seconds including builds. The new scenarios execute
Owner creation/editing/search/detail, local vehicle creation/edit/archive, and Customer personal
vehicle add/edit/archive. They independently read persisted state via real adapters. Turkish plate
normalization and foreign display preservation are checked on actual responses. Existing settings
and both recipient invitation scenarios passed in the same run. No real internet email was delivered;
fixtures consume the development memory mailbox. No physical-device or Android tests occurred.

The new PostgreSQL tests exercise actual ASP.NET HTTP/auth/session pipelines and database persistence,
including forged body profile/user IDs, cross-table vehicle IDs, mismatched nested parents, multiple
businesses and removed grants. Unit/PostgreSQL assertions also verify Turkish dotted-I name matching.
Initial test runs revealed and corrected fixture issues (a migration-count edit accidentally touched
a role-count assertion, and a tracked EF dependent prevented reaching the database FK check). No
security check was weakened. The existing Customer-home placeholder-copy assertion was updated while
retaining its pre-context-loading access checks. Detail-refresh stale-state clearing gained a regression.

Test runtime: separate PostgreSQL 17 Docker container `washflow-phase7-test`, tmpfs storage, dedicated
loopback port 55439 and disposable database; separate Development Kestrel on loopback 5189, temporary
signing material and suppressed logs; iPhone 18 Pro iOS 27.0 simulator. Secrets were kept in process
memory/environment and not emitted to reports/log output. Only the existing migration chain in that
disposable database was applied. The temporary listener/container were removed after testing; the
simulator started for testing was shut down. No existing development/production database was touched.

Ready for review and a controlled demo, with the scoped limitations above. No unresolved implementation
blocker was found. Suggested commit (not created):
`feat: add scoped customer and vehicle management (#3)`.

## Changed/new files

Total: 43 files.

- `backend/README.md`
- `backend/docs/migrations/20261027074840_CustomerAndVehicleOwnership.sql`
- `backend/src/WashFlow.Api/Features/Customers/CustomerVehicleEndpoints.cs`
- `backend/src/WashFlow.Api/Program.cs`
- `backend/src/WashFlow.Application/Customers/CustomerVehicleContracts.cs`
- `backend/src/WashFlow.Domain/Customers/LocalCustomer.cs`
- `backend/src/WashFlow.Domain/Customers/LocalVehicle.cs`
- `backend/src/WashFlow.Domain/Customers/OwnedVehicle.cs`
- `backend/src/WashFlow.Infrastructure/Customers/EfCustomerVehicleStore.cs`
- `backend/src/WashFlow.Infrastructure/DependencyInjection.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Configurations/CustomerVehicleConfiguration.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261027074840_CustomerAndVehicleOwnership.Designer.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261027074840_CustomerAndVehicleOwnership.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/WashFlowDbContextModelSnapshot.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/WashFlowDbContext.cs`
- `backend/tests/WashFlow.IntegrationTests/CustomerVehiclePostgresTests.cs`
- `backend/tests/WashFlow.IntegrationTests/PersistenceModelTests.cs`
- `backend/tests/WashFlow.UnitTests/CustomerVehicleValidationTests.cs`
- `backend/tests/WashFlow.UnitTests/IdentityModelTests.cs`
- `backend/tests/WashFlow.UnitTests/InfrastructureTests.cs`
- `docs/features/customer-vehicle-management-phase7.md`
- `docs/requirements/WashFlow_SRS_v0.2.md`
- `mobile/README.md`
- `mobile/integration_test/customer_vehicles_live_test.dart`
- `mobile/lib/app/app.dart`
- `mobile/lib/app/routing/app_router.dart`
- `mobile/lib/core/network/api_client.dart`
- `mobile/lib/features/auth/presentation/session_screen.dart`
- `mobile/lib/features/customer/README.md`
- `mobile/lib/features/customer/presentation/customer_home_screen.dart`
- `mobile/lib/features/customer_vehicles/data/api_customer_vehicle_service.dart`
- `mobile/lib/features/customer_vehicles/domain/customer_vehicles.dart`
- `mobile/lib/features/customer_vehicles/presentation/customer_directory_screen.dart`
- `mobile/lib/features/customer_vehicles/presentation/management_controllers.dart`
- `mobile/lib/features/customer_vehicles/presentation/record_editor_screen.dart`
- `mobile/lib/features/owner/README.md`
- `mobile/lib/features/owner/presentation/owner_home_screen.dart`
- `mobile/lib/main.dart`
- `mobile/test/customer_vehicle_controller_test.dart`
- `mobile/test/customer_vehicle_service_test.dart`
- `mobile/test/customer_vehicle_widget_test.dart`
- `mobile/test/session_navigation_test.dart`
- `mobile/test/support/customer_vehicle_fakes.dart`
