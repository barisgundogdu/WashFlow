# Service catalog, vehicle pricing and estimates — Phase 8

Traceability: GitHub Issue #5; SRS FR-06, FR-02/03/27 and the published-catalog portion of FR-22.
Baseline: clean `main`, `ed388e792dff0312449859e065d6e29f4f33b67f`. The user approved this
architecture and bounds before implementation. Full business discovery, appointment selection,
resource/work-area allocation, sequential stages, work orders, and historical charge snapshots
remain future work. Issue #5 is broader than this phase and must not be marked entirely complete.

## Model and exact pricing

`BusinessService` belongs to exactly one Business. It stores a stable UUID, display/search names,
optional plain-text description, enabled/published flags, UTC created/updated/archive timestamps
and an opaque PostgreSQL `xmin` version. `ServiceOffering` has composite PK `(ServiceId, Category)`
and a restricted service FK. Both new FKs use Restrict; no cascade deletion or destructive API exists.
Categories reuse Phase 7: `Car`, `Suv`, `Van`, `Pickup`, `Motorcycle`, `Other`, with the existing
Turkish Flutter labels. No customer, account or membership identifiers are part of catalog responses.

An offering row means the category is offered; no row means **not offered**, never a zero price.
Each offering specifies its own price, active duration, optional waiting duration, and employee
estimate. There are no inherited defaults or nullable override precedence rules. Duplicate categories
are rejected. Disabled, unpublished drafts can have zero offerings; enabled services need at least one.
Publication requires enabled status. The editor explicitly clears publication when disabling a service;
reenabling does not silently republish it. New and duplicated drafts start disabled/unpublished.

| Value | Representation and approved bound |
| --- | --- |
| Price | C# decimal, PostgreSQL `numeric(9,2)`, 0–9,999,999.99 TRY inclusive |
| Currency | Explicit `TRY`; other codes rejected |
| Active minutes | Integer 1–10,080 inclusive |
| Additional waiting minutes | null = unspecified; integer 0–43,200 inclusive; zero = explicitly none |
| Estimated employees | Integer 1–100 inclusive |
| Name / description | Required name 1–200; optional description ≤2,000 characters |

JSON price is a decimal **string**, e.g. `"450.10"`. Input accepts unsigned invariant decimal syntax
with at most seven whole digits and two fractional digits; no exponent, grouping, leading zeros
(other than zero itself), sign, whitespace or excess precision. The API rejects rather than rounds
excess precision. Output always has two fractional digits. Explicit zero is permitted and means free,
consistent with Issue #5. Flutter parses to integer kuruş, never double, and displays `450,10 TL` or
`1.000,00 TL`; form entry accepts a comma or decimal point without grouping. Amount and currency remain
separate fields for future currency policies. Supporting another currency requires explicit backend
validation/constraint and UI changes, including its approved precision.

PostgreSQL numeric typmods coerce fractional scale before CHECK constraints: direct SQL with extra
fractional digits can be rounded by PostgreSQL. All supported application writes validate the original
string before decimal conversion; direct SQL/import tools must enforce the same rule and are outside
the API boundary. The numeric type guarantees exact stored decimal arithmetic, not validation of an
untrusted original textual amount after coercion.

Active minutes represent estimated elapsed active service time with the stated estimated crew,
not per-person labor minutes. Additional waiting/drying/curing is distinct and does not imply bay
occupancy. Estimates are not promises, employee assignments, opening-hour calculations, or capacity
reservations. A later scheduler must explicitly define resource and stage semantics, business-time
handling and whether waiting occupies any separate area. No stage-allocation engine exists here.

## API contracts

All routes below are authenticated, use `Cache-Control: no-store`, safe ProblemDetails and the existing
60/minute per-IP authentication limiter. No request bodies, query values, credentials or raw provider
errors are logged by this feature. Hosting request-target logging remains filtered as established.

| Method | `/api/v1/businesses/{businessId}` suffix | Response |
| --- | --- | --- |
| GET | `/services` | Owner catalog page |
| POST | `/services` | 201 newly recorded service |
| GET | `/services/{serviceId}` | 200 current Owner service |
| PUT | `/services/{serviceId}` | 200 updated service |
| POST | `/services/{serviceId}/archive` | 204 confirmed archive |
| GET | `/published-services` | Customer-safe page for this known business only |

Create/replace input:

```json
{
  "name": "Dış yıkama",
  "description": "İsteğe bağlı açıklama",
  "enabled": true,
  "published": true,
  "offerings": [{
    "category": "Car", "price": "450.10", "currency": "TRY",
    "activeMinutes": 20, "waitingMinutes": null, "employees": 1
  }]
}
```

PUT additionally requires positive uint32 `version` from the last read. It replaces all editable fields
and the entire offering set atomically. Owner view adds `id`, `businessId`, `version`, `createdAt`,
`updatedAt`. Archive input is `{ "version": <last-read-version> }`. Ownership IDs are determined by
the authorized route, never accepted from the body. There is no delete/unarchive/transfer endpoint.
Service names need not be unique: distinct services retain distinct IDs.

List queries: optional `q` ≤100 characters; `page` 1–10,000 and `pageSize` 1–50, defaults 1/20.
Owner `status` is `all` (default), `enabled`, or `disabled`. Search uses existing Phase 7 name
normalization, including dotted/dotless I folding, with literal substring matching and no general
accent folding. Stable ordering is search name then UUID. Pages are `{items,page,pageSize,hasMore}`,
fetching only size+1 rows rather than an unbounded total. Concurrent edits can shift offset pages;
refresh to reconcile. No response is a cross-request pagination snapshot.

Published responses include only `id`, `businessId`, `name`, `description`, and offerings with
`category`, `price`, `currency`, `activeMinutes`, `waitingMinutes`. They exclude staffing estimates,
versions, timestamps and owner status fields. Query predicates require the **requested Business ID**,
no archive, enabled=true, published=true, and at least one offering. There is no fallback tenant or
business lookup by name. Missing businesses return empty pages without private existence disclosure.
Published reads use a repeatable-read transaction, so service fields and offerings come from one
consistent snapshot during concurrent edits/archival. No archived, disabled or unpublished service
can enter that snapshot. An in-flight read may legitimately reflect state before a concurrent archive
commits; subsequent reads exclude it. No public business discovery or customer browsing UI is added.

400 = invalid JSON/payload/query; 401 = invalid/missing live session; 403 = no active Owner for the
route business; 404 = absent, archived, or foreign nested service in an authorized business; 409 = stale
edit/archive version; 429 = rate limited. Uncertain network/server outcomes never imply a failed write
or trigger automatic replay. Validation occurs before lookup and discloses no stored private values.

## Authorization, transaction ordering and archival

| Principal | Owner management | Published known-business catalog |
| --- | --- | --- |
| Active Owner of requested Business | Allowed | Allowed |
| Owner of a different Business | Denied | Published fields only |
| Employee-only / Customer | Denied | Published fields only |
| Revoked membership, still-valid platform session | Denied management | Published fields only |
| Anonymous, revoked/invalid session | Denied | Denied |

Management uses the established transaction lock order: Business → actor membership → Owner grant.
Membership must be active and the exact business must retain the Owner grant. Every operation checks
again, including reads, updates and archive. Nested service lookup always includes business and archive
predicates. The same coarse business locks serialize competing changes and grant revocation under the
existing conventions. xmin comparisons prevent silently replaying an old draft; identical and
category-only saves also advance the parent version. EF optimistic concurrency failures become 409.
Offering replacement and service changes commit together; failed child persistence rolls back both.

Archive sets `ArchivedAt`, disables/unpublishes the service and retains its ID, fields and offering rows.
Archived services cannot be read/edited/selected through active catalog APIs. Domain `Available` means
`Enabled && ArchivedAt == null`. **Every future operational-record creation must recheck that predicate
and category applicability inside its authorization transaction**; possession of an old service ID,
old response, or retained historical FK must never authorize new use. There are no operational creation
endpoints yet to wire this rule into. Existing historical references must remain readable through their
own authorized history endpoints, without requiring the current service to be available. Future work
orders must snapshot the charged price, currency and service estimates; a current catalog price is
not a historical charge. Current offering replacement is not an audit/version history of prior prices.

## Flutter workflows and state boundaries

Owner home → **Hizmetler ve Fiyatlar** first refreshes backend contexts. A private selected-Owner
navigator route receives the exact business and a live generation guard. Employee selection and forged
root routes do not open management. App injection uses the existing shared HTTP client and authenticated
session/access-token boundary, including identity revision checks around token acquisition and responses.
No mutation, refresh-token request, or HTTP failure is automatically retried by this feature.

The list offers bounded pages, debounced search, status filter, price summary and Turkish availability
labels, plus loading/empty/error/retry controls. Opening details fetches a fresh record. The editor
supports category applicability, exact price, active/waiting minutes, staff estimates, enabled and
publication flags, save/cancel and confirmation before discarding a draft. Duplicate creates only an
unsaved inactive draft with no ID/version; explicit Save is required to POST a new service. Archive
requires confirmation and an unchanged current selected record/version. Confirmed saves re-GET the
record; confirmed archives re-GET the list. No confirmed-write success is inferred from a timeout.

Duplicate actions are blocked during writes. Conflict, missing record, uncertain write, or failed
post-save GET prevents further mutations until deliberate authoritative reconciliation. An unknown
creation outcome requires discarding the draft and checking the list before intentionally creating
again. No idempotency key or server-side duplicate-creation suppression is claimed. Back, refresh and
context switching confirm draft discard; logout and OS lifecycle invalidation intentionally clear
private state immediately. The bounded editor supports scrolling/keyboard dismissal and large text.

Logout/account replacement/context generation changes/suspension destroy the private navigator and its
controllers/dialogs/drafts. Generation and disposal guards suppress late reads/writes and debounced
searches; account revision checks also reject old authenticated transport responses. Authorization
failures clear feature state before asking the existing session controller to refresh server contexts.
A dispatched mutation can still commit after local cancellation; its result cannot populate a new tenant.
No drafts, prices, role grants or credentials are persisted in preferences/secure storage by this module.

## Migration review and safe future deployment

New migration: `20261027083159_ServiceCatalogAndOfferings` (sixth migration).
Review SQL: `backend/docs/migrations/20261027083159_ServiceCatalogAndOfferings.sql`.
Upgrade creates only BusinessServices/ServiceOfferings, their keys, checks and index, plus migration
history. No existing table alteration, update, delete, seed or invented pilot row occurs. Down drops
both catalog tables and loses their data; never use it casually once references/history exist.

The complete chain was exercised in unique schemas inside a **new** PostgreSQL 17 container,
`washflow-phase8-test`, with tmpfs storage on loopback port 55439. The live Development API on port
5189 uses only that new disposable database, Memory email and ephemeral signing material with logs
suppressed. Existing development/production databases and developer Docker services were not accessed,
migrated, reset or seeded. Connection details and credentials are not included in test output.

Future deployment requires separate explicit authorization: back up, review the complete pending
migration chain against a copy, apply all missing migrations in order (including Phase 6/7), and
coordinate binary rollout. Do not deploy a binary expecting these tables against an unmigrated schema.
Tests and this implementation do not authorize deployment or changes to existing databases.

## Pilot data and scope limitations

No production/development seeds or automatic catalog defaults were added. Tests create only synthetic
records in the disposable instance. The Phase 8 pilot examples are configurable elicitation inputs,
not implemented universal defaults. In particular, newer polishing/ceramic/engine/seat examples differ
from SRS §6.4; historical SRS evidence is preserved. Unspecified category applicability, curing duration,
and staff ranges must be resolved by the Owner before saving an offering, rather than guessed.

No appointment, slot, stage, work-area, work-order, payment, expense, subscription, notification,
customer-account linking or production-email implementation is part of Phase 8. No customer discovery
screens or employee operational catalog screen is claimed. Issue #5's stage/work-area, historical-charge
and appointment selection criteria remain future work. Physical-device/Android/release-HTTPS validation,
capacity performance and production migration execution remain outside the automated evidence below.

## Executed validation and file inventory

Validation on 27 October 2026:

| Gate | Executed result |
| --- | --- |
| `dotnet build backend/WashFlow.sln --no-restore` | Passed, zero warnings/errors |
| Backend unit tests | 130 passed, zero failed/skipped; 29 new cases |
| Backend integration tests | 103 passed, zero failed/skipped; 37 non-PostgreSQL + 66 PostgreSQL; eight new PostgreSQL cases |
| `flutter analyze` | Passed, no issues |
| `flutter test` | 264 passed, zero failures/skips; 44 new cases |
| Catalog iOS simulator scenario | Passed against real HTTP/Kestrel/disposable PostgreSQL |
| Combined live regression run | Six passed, zero failed/skipped, in 4 min 24 sec including simulator builds |
| `git diff --check` | Passed |

Backend tests cover exact price syntax/canonical formatting/bounds, stable categories, null/duplicate
and invalid offerings, positive versions, publication/draft rules, estimate bounds, HTTP CRUD,
search/filters/pages, anonymous/Employee/Customer/foreign Owner denial, nested foreign IDs, removed
grants, simultaneous edits, edit/archive races, no partial invalid writes, actual transaction rollback
on child constraint failure, restricted deletion, FK/price/currency/estimate constraints, safe published
projection and missing/disabled/unpublished/archived/empty service exclusion. Existing migration tests
now verify all six migrations and query all sixteen tables with no model drift.

Flutter tests cover exact kuruş/Turkish formatting, valid/invalid category/price parsing, exact request
serialization and authenticated headers, safe HTTP 400/401/403/404/409/429/503 mapping, account revision
checks, no network/timeout mutation retries, list/filter/pages/details, duplicate-submit prevention,
failed post-save GET, uncertainty/conflict locks, stale generations, foreign-tenant responses, revoked
grants, draft discard, duplicate drafts, confirmation and authoritative archive reload, logout, forged
routes, Employee denial, suspension/dialog cleanup, fresh Business B data and compact large-text forms
with keyboard insets. Existing auth/invitation/settings/customer-vehicle tests run unchanged.

The live catalog scenario creates and publishes an offering with an exact 450.10 TRY price, 20 active
minutes, 120 waiting minutes and one estimated employee; edits it, provokes a real stale-version 409,
reads the published projection, confirms archive, verifies empty active/published lists and logs out.
All records are synthetic test data, not pilot seeds. This uses the existing injected app harness and
real secure session storage, not an app-store/release bootstrap. No screenshots or sensitive dumps
are produced. The first widget runs exposed scrolling synchronization and a large-text overflow;
these were fixed and the complete 264-test suite passed afterward. No test was skipped or weakened.

The combined simulator run passed both invitation scenarios, business settings, both Owner/Customer
vehicle scenarios and the catalog scenario. A final catalog-only run additionally checks real
debounced search and back navigation after keeping the search field mounted while reads are pending.
The final catalog-only run passed (one scenario, 55 seconds excluding its 13.2-second Xcode build),
including real debounced search and back navigation. The isolated API was stopped, only the new
`washflow-phase8-test` container was removed, and the simulator booted for this task was shut down
after validation. No existing Docker service or database was modified.

### Changed/new files

Total: 37 files. Paths below are repository-relative; generated migration designer and snapshot are included.

- `README.md`
- `backend/README.md`
- `backend/docs/migrations/20261027083159_ServiceCatalogAndOfferings.sql`
- `backend/src/WashFlow.Api/Features/Catalog/CatalogEndpoints.cs`
- `backend/src/WashFlow.Api/Program.cs`
- `backend/src/WashFlow.Application/Catalog/CatalogContracts.cs`
- `backend/src/WashFlow.Domain/Catalog/BusinessService.cs`
- `backend/src/WashFlow.Infrastructure/Catalog/EfCatalogStore.cs`
- `backend/src/WashFlow.Infrastructure/DependencyInjection.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Configurations/CatalogConfiguration.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261027083159_ServiceCatalogAndOfferings.Designer.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/20261027083159_ServiceCatalogAndOfferings.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/Migrations/WashFlowDbContextModelSnapshot.cs`
- `backend/src/WashFlow.Infrastructure/Persistence/WashFlowDbContext.cs`
- `backend/tests/WashFlow.IntegrationTests/CatalogPostgresTests.cs`
- `backend/tests/WashFlow.IntegrationTests/PersistenceModelTests.cs`
- `backend/tests/WashFlow.UnitTests/CatalogValidationTests.cs`
- `backend/tests/WashFlow.UnitTests/IdentityModelTests.cs`
- `backend/tests/WashFlow.UnitTests/InfrastructureTests.cs`
- `docs/features/service-catalog-phase8.md`
- `docs/requirements/WashFlow_SRS_v0.2.md`
- `mobile/README.md`
- `mobile/integration_test/service_catalog_live_test.dart`
- `mobile/lib/app/app.dart`
- `mobile/lib/app/routing/app_router.dart`
- `mobile/lib/core/network/api_client.dart`
- `mobile/lib/features/auth/presentation/session_screen.dart`
- `mobile/lib/features/owner/presentation/owner_home_screen.dart`
- `mobile/lib/features/service_catalog/data/api_catalog_service.dart`
- `mobile/lib/features/service_catalog/domain/service_catalog.dart`
- `mobile/lib/features/service_catalog/presentation/catalog_controller.dart`
- `mobile/lib/features/service_catalog/presentation/catalog_screen.dart`
- `mobile/lib/main.dart`
- `mobile/test/catalog_controller_test.dart`
- `mobile/test/catalog_service_test.dart`
- `mobile/test/catalog_widget_test.dart`
- `mobile/test/support/catalog_fakes.dart`

## Independent audit hardening — 27 October 2026

Starting point was clean `main` at `8ace7e6f5f3f6eb813fc8371ee61ce90d55fe08d`.
This follow-up addresses M1/M2/L1/L2 from the independent Phase 8 audit, linked to Issue #5.
It does not complete the broader issue or add Phase 9 behavior.

- **M1:** Validation rejects unsupported original characters before trimming/normalization, then
  checks the final NFC service name, derived search name and optional description against storage
  limits. The existing surrogate/control-character policy remains unchanged. Normalization-expanding
  input now produces field validation errors instead of reaching PostgreSQL varchar errors. HTTP
  POST/PUT tests cover expansion beyond both limits, unchanged data/version after rejection, exact
  normalized boundaries, and canonical Turkish text persistence.
- **M2:** New PostgreSQL tests pause the published read immediately before its offering SELECT,
  after the parent SELECT establishes the snapshot. A separate request commits replacement,
  disablement or archival before the read resumes. Each test verifies the original consistent
  response and the authoritative subsequent response. Task-completion barriers control ordering;
  timeouts only bound failures. Further tests exercise revoked membership with its Owner grant
  retained, Employee/Customer denial for all private operations, and revoked sessions across all
  six catalog endpoints.
- **L1:** Shared Flutter field/draft validation checks the same original unsupported characters,
  .NET whitespace trimming and NFC storage lengths. The pure-Dart `unorm_dart` dependency is locked
  to 0.3.3. Unsupported inputs produce actionable Turkish name/description errors before HTTP.
  Tests cover supplementary characters, unpaired surrogates, C1 controls, permitted description
  whitespace, expanding/composing normalization, FEFF handling, and absence of HTTP submission.
- **L2:** List responses fetch offerings once for the returned page IDs, skipping the child query
  for empty pages. Existing tenant predicates, look-ahead pagination, ordering, authorization locks,
  transaction isolation and published projection are preserved. Query-count tests exercise private
  and published lists, both pages, empty search results, prices/categories and foreign-tenant data.

Verification used a new `postgres:17` container, `washflow-phase8-hardening-20261027`, with tmpfs
storage and loopback port 55439; the actual server reported PostgreSQL 17.11. All backend PostgreSQL
fixtures migrated uniquely named schemas only inside this instance. The six existing migrations
were also applied to this disposable database's fresh public schema for the loopback live API.
No existing development/production database was accessed or migrated. No migration, model mapping,
snapshot or SQL schema file changed.

| Gate | Independently executed hardening result |
| --- | --- |
| `dotnet build backend/WashFlow.sln --no-restore -m:1 -nr:false` | Passed; zero warnings/errors |
| `dotnet test backend/WashFlow.sln --no-build --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'` with disposable `WASHFLOW_TEST_POSTGRES` | Integration: 111 passed, zero failed/skipped, including eight new hardening tests |
| `dotnet test backend/tests/WashFlow.UnitTests/WashFlow.UnitTests.csproj --no-restore -m:1 -nr:false --logger 'console;verbosity=minimal'` | Final unit run: 139 passed, zero failed/skipped; nine new cases |
| `flutter analyze` from `mobile/` | Passed; no issues |
| `flutter test --reporter expanded` from `mobile/` | 278 passed, zero failed/skipped; fourteen new cases |
| `flutter test integration_test --dart-define=PHASE5_API_URL=http://127.0.0.1:5189 -d B46C9E93-8E89-47E1-AA7F-DA92D7C7987C --reporter expanded` from `mobile/` | Six passed, zero failed/skipped, in 4 min 38 sec including simulator builds |
| `dotnet ef migrations has-pending-model-changes --project backend/src/WashFlow.Infrastructure --startup-project backend/src/WashFlow.Api --no-build` without a connection string | No pending model changes |

The first unit run had 138 passing cases and one failing new test: C# attribute serialization
replaced an intentionally unpaired surrogate before validation. The test now constructs that
invalid UTF-16 string at runtime and asserts both field errors; no validation was weakened.
Initial sandbox restrictions on MSBuild sockets were resolved by running verification with the
necessary local-process access. EF CLI 10.0.8 reported its version warning against runtime 10.0.13;
migration application, model consistency and the integration suite nevertheless completed.

The live run passed both customer/vehicle scenarios, the catalog scenario, business settings and
both invitation scenarios on an iPhone 18 Pro / iOS 27.0 simulator. The catalog scenario additionally
checks Turkish field errors without creating a service, then round-trips a decomposed Turkish
description as canonical NFC through real HTTP and PostgreSQL. The isolated API was stopped, the
new disposable container removed, and the simulator booted for this task shut down after testing.
No requested automated verification gate was left unexecuted. Physical devices, Android and
release/production deployment remain outside this pass. Full diff review found no Phase 9 or schema
changes, no removed tests and no staged changes; `git diff --check` passed.

Additional changed files for hardening include
`backend/tests/WashFlow.IntegrationTests/CatalogHardeningPostgresTests.cs`,
`mobile/pubspec.yaml`, and `mobile/pubspec.lock`; the other edits are within existing Phase 8
implementation/tests and this evidence document. Work remains uncommitted for review.
